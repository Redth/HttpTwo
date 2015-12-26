/*
 * Copyright 2015 Ringo Leese
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.IO;

namespace HttpTwo.HPack
{
    public class Decoder
    {
        private static IOException DECOMPRESSION_EXCEPTION = new IOException("decompression failure");

        private static byte[] EMPTY = { };

        private DynamicTable dynamicTable;

        private int maxHeaderSize;
        private int maxDynamicTableSize;
        private int encoderMaxDynamicTableSize;
        private bool maxDynamicTableSizeChangeRequired;

        private long headerSize;
        private State state;
        private HPackUtil.IndexType indexType;
        private int index;
        private bool huffmanEncoded;
        private int skipLength;
        private int nameLength;
        private int valueLength;
        private byte[] name;

        public enum State
        {
            READ_HEADER_REPRESENTATION,
            READ_MAX_DYNAMIC_TABLE_SIZE,
            READ_INDEXED_HEADER,
            READ_INDEXED_HEADER_NAME,
            READ_LITERAL_HEADER_NAME_LENGTH_PREFIX,
            READ_LITERAL_HEADER_NAME_LENGTH,
            READ_LITERAL_HEADER_NAME,
            SKIP_LITERAL_HEADER_NAME,
            READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX,
            READ_LITERAL_HEADER_VALUE_LENGTH,
            READ_LITERAL_HEADER_VALUE,
            SKIP_LITERAL_HEADER_VALUE
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="hpack.Decoder"/> class.
        /// </summary>
        /// <param name="maxHeaderSize">Max header size.</param>
        /// <param name="maxHeaderTableSize">Max header table size.</param>
        public Decoder(int maxHeaderSize, int maxHeaderTableSize)
        {
            this.dynamicTable = new DynamicTable(maxHeaderTableSize);
            this.maxHeaderSize = maxHeaderSize;
            this.maxDynamicTableSize = maxHeaderTableSize;
            this.encoderMaxDynamicTableSize = maxHeaderTableSize;
            this.maxDynamicTableSizeChangeRequired = false;
            this.Reset();
        }

        private void Reset()
        {
            this.headerSize = 0;
            this.state = State.READ_HEADER_REPRESENTATION;
            this.indexType = HPackUtil.IndexType.NONE;
        }

        /// <summary>
        /// Decode the header block into header fields.
        /// </summary>
        /// <param name="input">Input.</param>
        /// <param name="headerListener">Header listener.</param>
        public void Decode(BinaryReader input, AddHeaderDelegate addHeaderDelegate)
        {
            while(input.BaseStream.Length - input.BaseStream.Position > 0) {
                switch(this.state) {
                case State.READ_HEADER_REPRESENTATION:
                    sbyte b = input.ReadSByte();
                    if (maxDynamicTableSizeChangeRequired && (b & 0xE0) != 0x20) {
                        // Encoder MUST signal maximum dynamic table size change
                        throw new IOException("max dynamic table size change required");
                    }
                    if (b < 0) {
                        // Indexed Header Field
                        index = b & 0x7F;
                        if (index == 0) {
                            throw new IOException("illegal index value (" + index + ")");
                        } else if (index == 0x7F) {
                            state = State.READ_INDEXED_HEADER;
                        } else {
                            this.IndexHeader(index, addHeaderDelegate);
                        }
                    } else if ((b & 0x40) == 0x40) {
                        // Literal Header Field with Incremental Indexing
                        indexType = HPackUtil.IndexType.INCREMENTAL;
                        index = b & 0x3F;
                        if (index == 0) {
                            state = State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                        } else if (index == 0x3F) {
                            state = State.READ_INDEXED_HEADER_NAME;
                        } else {
                            // Index was stored as the prefix
                            this.ReadName(index);
                            state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                        }
                    } else if ((b & 0x20) == 0x20) {
                        // Dynamic Table Size Update
                        index = b & 0x1F;
                        if (index == 0x1F) {
                            state = State.READ_MAX_DYNAMIC_TABLE_SIZE;
                        } else {
                            this.SetDynamicTableSize(index);
                            state = State.READ_HEADER_REPRESENTATION;
                        }
                    } else {
                        // Literal Header Field without Indexing / never Indexed
                        indexType = ((b & 0x10) == 0x10) ? HPackUtil.IndexType.NEVER : HPackUtil.IndexType.NONE;
                        index = b & 0x0F;
                        if (index == 0) {
                            state = State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX;
                        } else if (index == 0x0F) {
                            state = State.READ_INDEXED_HEADER_NAME;
                        } else {
                            // Index was stored as the prefix
                            this.ReadName(index);
                            state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                        }
                    }
                    break;

                case State.READ_MAX_DYNAMIC_TABLE_SIZE:
                    int maxSize = Decoder.DecodeULE128(input);
                    if (maxSize == -1) {
                        return;
                    }

                    // Check for numerical overflow
                    if (maxSize > int.MaxValue - index) {
                        throw DECOMPRESSION_EXCEPTION;
                    }

                    this.SetDynamicTableSize(index + maxSize);
                    state = State.READ_HEADER_REPRESENTATION;
                    break;

                case State.READ_INDEXED_HEADER:
                    int headerIndex = Decoder.DecodeULE128(input);
                    if (headerIndex == -1) {
                        return;
                    }

                    // Check for numerical overflow
                    if (headerIndex > int.MaxValue - index) {
                        throw DECOMPRESSION_EXCEPTION;
                    }

                    this.IndexHeader(index + headerIndex, addHeaderDelegate);
                    state = State.READ_HEADER_REPRESENTATION;
                    break;

                case State.READ_INDEXED_HEADER_NAME:
                    // Header Name matches an entry in the Header Table
                    int nameIndex = Decoder.DecodeULE128(input);
                    if (nameIndex == -1) {
                        return;
                    }

                    // Check for numerical overflow
                    if (nameIndex > int.MaxValue - index) {
                        throw DECOMPRESSION_EXCEPTION;
                    }

                    this.ReadName(index + nameIndex);
                    state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                    break;

                case State.READ_LITERAL_HEADER_NAME_LENGTH_PREFIX:
                    b = input.ReadSByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    if (index == 0x7f) {
                        state = State.READ_LITERAL_HEADER_NAME_LENGTH;
                    } else {
                        nameLength = index;

                        // Disallow empty names -- they cannot be represented in HTTP/1.x
                        if (nameLength == 0) {
                            throw DECOMPRESSION_EXCEPTION;
                        }

                        // Check name length against max header size
                        if (this.ExceedsMaxHeaderSize(nameLength)) {
                            if (indexType == HPackUtil.IndexType.NONE) {
                                // Name is unused so skip bytes
                                name = EMPTY;
                                this.skipLength = nameLength;
                                state = State.SKIP_LITERAL_HEADER_NAME;
                                break;
                            }

                            // Check name length against max dynamic table size
                            if (nameLength + HeaderField.HEADER_ENTRY_OVERHEAD > this.dynamicTable.Capacity) {
                                this.dynamicTable.Clear();
                                name = EMPTY;
                                this.skipLength = nameLength;
                                state = State.SKIP_LITERAL_HEADER_NAME;
                                break;
                            }
                        }
                        state = State.READ_LITERAL_HEADER_NAME;
                    }
                    break;

                case State.READ_LITERAL_HEADER_NAME_LENGTH:
                    // Header Name is a Literal String
                    nameLength = Decoder.DecodeULE128(input);
                    if (nameLength == -1) {
                        return;
                    }

                    // Check for numerical overflow
                    if (nameLength > int.MaxValue - index) {
                        throw DECOMPRESSION_EXCEPTION;
                    }
                    nameLength += index;

                    // Check name length against max header size
                    if (this.ExceedsMaxHeaderSize(nameLength)) {
                        if (indexType == HPackUtil.IndexType.NONE) {
                            // Name is unused so skip bytes
                            name = EMPTY;
                            this.skipLength = nameLength;
                            state = State.SKIP_LITERAL_HEADER_NAME;
                            break;
                        }

                        // Check name length against max dynamic table size
                        if (nameLength + HeaderField.HEADER_ENTRY_OVERHEAD > this.dynamicTable.Capacity) {
                            this.dynamicTable.Clear();
                            name = EMPTY;
                            this.skipLength = nameLength;
                            state = State.SKIP_LITERAL_HEADER_NAME;
                            break;
                        }
                    }
                    state = State.READ_LITERAL_HEADER_NAME;
                    break;

                case State.READ_LITERAL_HEADER_NAME:
                    // Wait until entire name is readable
                    if (input.BaseStream.Length - input.BaseStream.Position < nameLength) {
                        return;
                    }

                    name = this.ReadStringLiteral(input, nameLength);
                    state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                    break;

                case State.SKIP_LITERAL_HEADER_NAME:

                    this.skipLength -= (int)input.BaseStream.Seek(this.skipLength, SeekOrigin.Current);
                    if (this.skipLength < 0) {
                        this.skipLength = 0;
                    }
                    if (this.skipLength == 0) {
                        state = State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX;
                    }
                    break;

                case State.READ_LITERAL_HEADER_VALUE_LENGTH_PREFIX:
                    b = input.ReadSByte();
                    huffmanEncoded = (b & 0x80) == 0x80;
                    index = b & 0x7F;
                    if (index == 0x7f) {
                        state = State.READ_LITERAL_HEADER_VALUE_LENGTH;
                    } else {
                        this.valueLength = index;

                        // Check new header size against max header size
                        long newHeaderSize1 = (long)nameLength + (long)this.valueLength;
                        if (this.ExceedsMaxHeaderSize(newHeaderSize1)) {
                            // truncation will be reported during endHeaderBlock
                            headerSize = maxHeaderSize + 1;

                            if (indexType == HPackUtil.IndexType.NONE) {
                                // Value is unused so skip bytes
                                state = State.SKIP_LITERAL_HEADER_VALUE;
                                break;
                            }

                            // Check new header size against max dynamic table size
                            if (newHeaderSize1 + HeaderField.HEADER_ENTRY_OVERHEAD > this.dynamicTable.Capacity) {
                                this.dynamicTable.Clear();
                                state = State.SKIP_LITERAL_HEADER_VALUE;
                                break;
                            }
                        }

                        if (this.valueLength == 0) {
                            this.InsertHeader(addHeaderDelegate, name, EMPTY, indexType);
                            state = State.READ_HEADER_REPRESENTATION;
                        } else {
                            state = State.READ_LITERAL_HEADER_VALUE;
                        }
                    }
                    break;

                case State.READ_LITERAL_HEADER_VALUE_LENGTH:
                    // Header Value is a Literal String
                    this.valueLength = Decoder.DecodeULE128(input);
                    if (this.valueLength == -1) {
                        return;
                    }

                    // Check for numerical overflow
                    if (this.valueLength > int.MaxValue - index) {
                        throw DECOMPRESSION_EXCEPTION;
                    }
                    this.valueLength += index;

                    // Check new header size against max header size
                    long newHeaderSize2 = (long)nameLength + (long)this.valueLength;
                    if (newHeaderSize2 + headerSize > maxHeaderSize) {
                        // truncation will be reported during endHeaderBlock
                        headerSize = maxHeaderSize + 1;

                        if (indexType == HPackUtil.IndexType.NONE) {
                            // Value is unused so skip bytes
                            state = State.SKIP_LITERAL_HEADER_VALUE;
                            break;
                        }

                        // Check new header size against max dynamic table size
                        if (newHeaderSize2 + HeaderField.HEADER_ENTRY_OVERHEAD > this.dynamicTable.Capacity) {
                            this.dynamicTable.Clear();
                            state = State.SKIP_LITERAL_HEADER_VALUE;
                            break;
                        }
                    }
                    state = State.READ_LITERAL_HEADER_VALUE;
                    break;

                case State.READ_LITERAL_HEADER_VALUE:
                    // Wait until entire value is readable
                    if (input.BaseStream.Length - input.BaseStream.Position < this.valueLength) {
                        return;
                    }

                    byte[] value = this.ReadStringLiteral(input, this.valueLength);
                    this.InsertHeader(addHeaderDelegate, name, value, indexType);
                    state = State.READ_HEADER_REPRESENTATION;
                    break;

                case State.SKIP_LITERAL_HEADER_VALUE:
                    this.valueLength -= (int)input.BaseStream.Seek(this.valueLength, SeekOrigin.Current);
                    if (this.valueLength < 0) {
                        this.valueLength = 0;
                    }
                    if (this.valueLength == 0) {
                        state = State.READ_HEADER_REPRESENTATION;
                    }
                    break;

                default:
                    throw new Exception("should not reach here");
                }
            }
        }

        /// <summary>
        /// End the current header block. Returns if the header field has been truncated.
        /// This must be called after the header block has been completely decoded.
        /// </summary>
        /// <returns><c>true</c>, if header block was ended, <c>false</c> otherwise.</returns>
        public bool EndHeaderBlock()
        {
            bool truncated = headerSize > maxHeaderSize;
            this.Reset();
            return truncated;
        }

        /// <summary>
        /// Set the maximum table size.
        /// If this is below the maximum size of the dynamic table used by the encoder,
        /// the beginning of the next header block MUST signal this change.
        /// </summary>
        /// <param name="maxHeaderTableSize">Max header table size.</param>
        public void SetMaxHeaderTableSize(int maxHeaderTableSize)
        {
            maxDynamicTableSize = maxHeaderTableSize;
            if (maxDynamicTableSize < encoderMaxDynamicTableSize) {
                // decoder requires less space than encoder
                // encoder MUST signal this change
                this.maxDynamicTableSizeChangeRequired = true;
                this.dynamicTable.SetCapacity(maxDynamicTableSize);
            }
        }

        /// <summary>
        /// Return the maximum table size.
        /// This is the maximum size allowed by both the encoder and the decoder.
        /// </summary>
        /// <returns>The max header table size.</returns>
        public int GetMaxHeaderTableSize()
        {
            return this.dynamicTable.Capacity;
        }

        /// <summary>
        /// Return the number of header fields in the dynamic table.
        /// Exposed for testing.
        /// </summary>
        int Length()
        {
            return this.dynamicTable.Length();
        }

        /// <summary>
        /// Return the size of the dynamic table.
        /// Exposed for testing.
        /// </summary>
        int Size()
        {
            return this.dynamicTable.Size;
        }

        /// <summary>
        /// Return the header field at the given index.
        /// Exposed for testing.
        /// </summary>
        /// <returns>The header field.</returns>
        /// <param name="index">Index.</param>
        HeaderField GetHeaderField(int index)
        {
            return this.dynamicTable.GetEntry(index + 1);
        }

        private void SetDynamicTableSize(int dynamicTableSize)
        {
            if (dynamicTableSize > this.maxDynamicTableSize) {
                throw new IOException("invalid max dynamic table size");
            }
            this.encoderMaxDynamicTableSize = dynamicTableSize;
            this.maxDynamicTableSizeChangeRequired = false;
            this.dynamicTable.SetCapacity(dynamicTableSize);
        }

        private void ReadName(int index)
        {
            if (index <= StaticTable.Length) {
                HeaderField headerField = StaticTable.GetEntry(index);
                name = headerField.Name;
            } else if (index - StaticTable.Length <= this.dynamicTable.Length()) {
                HeaderField headerField = this.dynamicTable.GetEntry(index - StaticTable.Length);
                name = headerField.Name;
            } else {
                throw new IOException("illegal index value (" + index + ")");
            }
        }

        private void IndexHeader(int index, AddHeaderDelegate addHeaderDelegate)
        {
            if (index <= StaticTable.Length) {
                HeaderField headerField = StaticTable.GetEntry(index);
                this.AddHeader(addHeaderDelegate, headerField.Name, headerField.Value, false);
            } else if (index - StaticTable.Length <= this.dynamicTable.Length()) {
                HeaderField headerField = this.dynamicTable.GetEntry(index - StaticTable.Length);
                this.AddHeader(addHeaderDelegate, headerField.Name, headerField.Value, false);
            } else {
                throw new IOException("illegal index value (" + index + ")");
            }
        }

        private void InsertHeader(AddHeaderDelegate addHeaderDelegate, byte[] name, byte[] value, HPackUtil.IndexType indexType)
        {
            this.AddHeader(addHeaderDelegate, name, value, indexType == HPackUtil.IndexType.NEVER);

            switch(indexType) {
            case HPackUtil.IndexType.NONE:
            case HPackUtil.IndexType.NEVER:
                break;

            case HPackUtil.IndexType.INCREMENTAL:
                this.dynamicTable.Add(new HeaderField(name, value));
                break;

            default:
                throw new Exception("should not reach here");
            }
        }

        private void AddHeader(AddHeaderDelegate addHeaderDelegate, byte[] name, byte[] value, bool sensitive)
        {
            if (name.Length == 0) {
                throw new ArgumentException("name is empty");
            }
            long newSize = headerSize + name.Length + value.Length;
            if (newSize <= maxHeaderSize) {
                addHeaderDelegate (name, value, sensitive);
                headerSize = (int)newSize;
            } else {
                // truncation will be reported during endHeaderBlock
                headerSize = maxHeaderSize + 1;
            }
        }

        private bool ExceedsMaxHeaderSize(long size)
        {
            // Check new header size against max header size
            if (size + headerSize <= maxHeaderSize) {
                return false;
            }

            // truncation will be reported during endHeaderBlock
            headerSize = maxHeaderSize + 1;
            return true;
        }

        private byte[] ReadStringLiteral(BinaryReader input, int length)
        {
            byte[] buf = new byte[length];
            int lengthToRead = length;
            if (input.BaseStream.Length - input.BaseStream.Position < length) {
                lengthToRead = (int)input.BaseStream.Length - (int)input.BaseStream.Position;
            }
            int readBytes = input.Read(buf, 0, lengthToRead);
            if (readBytes != length) {
                throw DECOMPRESSION_EXCEPTION;
            }

            if (huffmanEncoded) {
                return Huffman.DECODER.Decode(buf);
            } else {
                return buf;
            }
        }

        // Unsigned Little Endian Base 128 Variable-Length Integer Encoding
        private static int DecodeULE128(BinaryReader input)
        {
            long markedPosition = input.BaseStream.Position;
            int result = 0;
            int shift = 0;
            while(shift < 32) {
                if (input.BaseStream.Length - input.BaseStream.Position == 0) {
                    // Buffer does not contain entire integer,
                    // reset reader index and return -1.
                    input.BaseStream.Position = markedPosition;
                    return -1;
                }
                sbyte b = input.ReadSByte();
                if (shift == 28 && (b & 0xF8) != 0) {
                    break;
                }
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) {
                    return result;
                }
                shift += 7;
            }
            // Value exceeds Integer.MAX_VALUE
            input.BaseStream.Position = markedPosition;
            throw DECOMPRESSION_EXCEPTION;
        }
    }
}