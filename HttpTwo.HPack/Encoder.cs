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
using System.Text;

namespace HttpTwo.HPack
{
    public class Encoder
    {
        private static int BUCKET_SIZE = 17;
        private static byte[] EMPTY = { };

        // for testing
        private bool useIndexing;
        private bool forceHuffmanOn;
        private bool forceHuffmanOff;

        // a linked hash map of header fields
        private HeaderEntry[] headerFields = new HeaderEntry[BUCKET_SIZE];
        private HeaderEntry head = new HeaderEntry(-1, EMPTY, EMPTY, int.MaxValue, null);
        private int size;
        private int capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="hpack.Encoder"/> class.
        /// </summary>
        /// <param name="maxHeaderTableSize">Max header table size.</param>
        public Encoder(int maxHeaderTableSize)
        {
            this.Init(maxHeaderTableSize, true, false, false);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="hpack.Encoder"/> class.
        /// for testing only.
        /// </summary>
        /// <param name="maxHeaderTableSize">Max header table size.</param>
        /// <param name="useIndexing">If set to <c>true</c> use indexing.</param>
        /// <param name="forceHuffmanOn">If set to <c>true</c> force huffman on.</param>
        /// <param name="forceHuffmanOff">If set to <c>true</c> force huffman off.</param>
        public Encoder(int maxHeaderTableSize, bool useIndexing, bool forceHuffmanOn, bool forceHuffmanOff)
        {
            this.Init(maxHeaderTableSize, useIndexing, forceHuffmanOn, forceHuffmanOff);
        }

        private void Init(int maxHeaderTableSize, bool useIndexing, bool forceHuffmanOn, bool forceHuffmanOff)
        {
            if (maxHeaderTableSize < 0) {
                throw new ArgumentException("Illegal Capacity: " + maxHeaderTableSize);
            }
            this.useIndexing = useIndexing;
            this.forceHuffmanOn = forceHuffmanOn;
            this.forceHuffmanOff = forceHuffmanOff;
            this.capacity = maxHeaderTableSize;
            head.Before = head.After = head;
        }

        public void EncodeHeader(BinaryWriter output, string name, string value)
        {
            this.EncodeHeader(output, name, value, false);
        }

        public void EncodeHeader(BinaryWriter output, string name, string value, bool sensitive)
        {
            this.EncodeHeader(output, Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(value), sensitive);
        }

        public void EncodeHeader(BinaryWriter output, byte[] name, byte[] value)
        {
            this.EncodeHeader(output, name, value, false);
        }

        /// <summary>
        /// Encode the header field into the header block.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        /// <param name="sensitive">If set to <c>true</c> sensitive.</param>
        public void EncodeHeader(BinaryWriter output, byte[] name, byte[] value, bool sensitive)
        {
            // If the header value is sensitive then it must never be indexed
            if (sensitive) {
                int nameIndex = this.GetNameIndex(name);
                this.EncodeLiteral(output, name, value, HPackUtil.IndexType.NEVER, nameIndex);
                return;
            }

            // If the peer will only use the static table
            if (this.capacity == 0) {
                int staticTableIndex = StaticTable.GetIndex(name, value);
                if (staticTableIndex == -1) {
                    int nameIndex = StaticTable.GetIndex(name);
                    this.EncodeLiteral(output, name, value, HPackUtil.IndexType.NONE, nameIndex);
                } else {
                    Encoder.EncodeInteger(output, 0x80, 7, staticTableIndex);
                }
                return;
            }

            int headerSize = HeaderField.SizeOf(name, value);

            // If the headerSize is greater than the max table size then it must be encoded literally
            if (headerSize > this.capacity) {
                int nameIndex = this.GetNameIndex(name);
                this.EncodeLiteral(output, name, value, HPackUtil.IndexType.NONE, nameIndex);
                return;
            }

            HeaderEntry headerField = this.GetEntry(name, value);
            if (headerField != null) {
                int index = this.GetIndex(headerField.Index) + StaticTable.Length;
                // Section 6.1. Indexed Header Field Representation
                Encoder.EncodeInteger(output, 0x80, 7, index);
            } else {
                int staticTableIndex = StaticTable.GetIndex(name, value);
                if (staticTableIndex != -1) {
                    // Section 6.1. Indexed Header Field Representation
                    Encoder.EncodeInteger(output, 0x80, 7, staticTableIndex);
                } else {
                    int nameIndex = this.GetNameIndex(name);
                    if (useIndexing) {
                        this.EnsureCapacity(headerSize);
                    }
                    var indexType = useIndexing ? HPackUtil.IndexType.INCREMENTAL : HPackUtil.IndexType.NONE;
                    this.EncodeLiteral(output, name, value, indexType, nameIndex);
                    if (useIndexing) {
                        this.Add(name, value);
                    }
                }
            }
        }

        /// <summary>
        /// Set the maximum table size.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="maxHeaderTableSize">Max header table size.</param>
        public void SetMaxHeaderTableSize(BinaryWriter output, int maxHeaderTableSize)
        {
            if (maxHeaderTableSize < 0) {
                throw new ArgumentException("Illegal Capacity: " + maxHeaderTableSize);
            }
            if (this.capacity == maxHeaderTableSize) {
                return;
            }
            this.capacity = maxHeaderTableSize;
            this.EnsureCapacity(0);
            Encoder.EncodeInteger(output, 0x20, 5, maxHeaderTableSize);
        }

        /// <summary>
        /// Return the maximum table size.
        /// </summary>
        /// <returns>The max header table size.</returns>
        public int GetMaxHeaderTableSize()
        {
            return this.capacity;
        }

        /// <summary>
        /// Encode integer according to Section 5.1.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="mask">Mask.</param>
        /// <param name="n">N.</param>
        /// <param name="i">The index.</param>
        private static void EncodeInteger(BinaryWriter output, int mask, int n, int i)
        {
            if (n < 0 || n > 8) {
                throw new ArgumentException("N: " + n);
            }
            int nbits = 0xFF >> (8 - n);
            if (i < nbits) {
                output.Write((byte)(mask | i));
            } else {
                output.Write((byte)(mask | nbits));
                int length = i - nbits;
                while(true) {
                    if ((length & ~0x7F) == 0) {
                        output.Write((byte)length);
                        return;
                    } else {
                        output.Write((byte)((length & 0x7F) | 0x80));
                        length >>= 7;
                    }
                }
            }
        }

        /// <summary>
        /// Encode string literal according to Section 5.2.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="stringLiteral">String literal.</param>
        private void EncodeStringLiteral(BinaryWriter output, byte[] stringLiteral)
        {
            int huffmanLength = Huffman.ENCODER.GetEncodedLength(stringLiteral);
            if ((huffmanLength < stringLiteral.Length && !forceHuffmanOff) || forceHuffmanOn) {
                Encoder.EncodeInteger(output, 0x80, 7, huffmanLength);
                Huffman.ENCODER.Encode(output, stringLiteral);
            } else {
                Encoder.EncodeInteger(output, 0x00, 7, stringLiteral.Length);
                output.Write(stringLiteral, 0, stringLiteral.Length);
            }
        }

        /// <summary>
        /// Encode literal header field according to Section 6.2.
        /// </summary>
        /// <param name="output">Output.</param>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        /// <param name="indexType">Index type.</param>
        /// <param name="nameIndex">Name index.</param>
        private void EncodeLiteral(BinaryWriter output, byte[] name, byte[] value, HPackUtil.IndexType indexType, int nameIndex)
        {
            int mask;
            int prefixBits;
            switch(indexType) {
            case HPackUtil.IndexType.INCREMENTAL:
                mask = 0x40;
                prefixBits = 6;
                break;

            case HPackUtil.IndexType.NONE:
                mask = 0x00;
                prefixBits = 4;
                break;

            case HPackUtil.IndexType.NEVER:
                mask = 0x10;
                prefixBits = 4;
                break;

            default:
                throw new Exception("should not reach here");
            }
            Encoder.EncodeInteger(output, mask, prefixBits, nameIndex == -1 ? 0 : nameIndex);
            if (nameIndex == -1) {
                this.EncodeStringLiteral(output, name);
            }
            this.EncodeStringLiteral(output, value);
        }

        private int GetNameIndex(byte[] name)
        {
            int index = StaticTable.GetIndex(name);
            if (index == -1) {
                index = this.GetIndex(name);
                if (index >= 0) {
                    index += StaticTable.Length;
                }
            }
            return index;
        }

        /// <summary>
        /// Ensure that the dynamic table has enough room to hold 'headerSize' more bytes.
        /// Removes the oldest entry from the dynamic table until sufficient space is available.
        /// </summary>
        /// <param name="headerSize">Header size.</param>
        private void EnsureCapacity(int headerSize)
        {
            while(this.size + headerSize > this.capacity) {
                int index = this.Length();
                if (index == 0) {
                    break;
                }
                this.Remove();
            }
        }

        /// <summary>
        /// Return the number of header fields in the dynamic table.
        /// Exposed for testing.
        /// </summary>
        int Length()
        {
            return this.size == 0 ? 0 : this.head.After.Index - this.head.Before.Index + 1;
        }

        /// <summary>
        /// Return the size of the dynamic table.
        /// Exposed for testing.
        /// </summary>
        /// <returns>The size.</returns>
        int GetSize()
        {
            return this.size;
        }

        /// <summary>
        /// Return the header field at the given index.
        /// Exposed for testing.
        /// </summary>
        /// <returns>The header field.</returns>
        /// <param name="index">Index.</param>
        HeaderField GetHeaderField(int index)
        {
            HeaderEntry entry = head;
            while(index-- >= 0) {
                entry = entry.Before;
            }
            return entry;
        }

        /// <summary>
        /// Returns the header entry with the lowest index value for the header field.
        /// Returns null if header field is not in the dynamic table.
        /// </summary>
        /// <returns>The entry.</returns>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        private HeaderEntry GetEntry(byte[] name, byte[] value)
        {
            if (this.Length() == 0 || name == null || value == null) {
                return null;
            }
            int h = Encoder.Hash(name);
            int i = Encoder.Index(h);
            for(HeaderEntry e = headerFields[i]; e != null; e = e.Next) {
                if (e.Hash == h && HPackUtil.Equals(name, e.Name) && HPackUtil.Equals(value, e.Value)) {
                    return e;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the lowest index value for the header field name in the dynamic table.
        /// Returns -1 if the header field name is not in the dynamic table.
        /// </summary>
        /// <returns>The index.</returns>
        /// <param name="name">Name.</param>
        private int GetIndex(byte[] name)
        {
            if (this.Length() == 0 || name == null) {
                return -1;
            }
            int h = Encoder.Hash(name);
            int i = Encoder.Index(h);
            int index = -1;
            for(HeaderEntry e = headerFields[i]; e != null; e = e.Next) {
                if (e.Hash == h && HPackUtil.Equals(name, e.Name)) {
                    index = e.Index;
                    break;
                }
            }
            return this.GetIndex(index);
        }

        /// <summary>
        /// Compute the index into the dynamic table given the index in the header entry.
        /// </summary>
        /// <returns>The index.</returns>
        /// <param name="index">Index.</param>
        private int GetIndex(int index)
        {
            if (index == -1) {
                return index;
            }
            return index - head.Before.Index + 1;
        }

        /// <summary>
        /// Add the header field to the dynamic table.
        /// Entries are evicted from the dynamic table until the size of the table
        /// and the new header field is less than the table's capacity.
        /// If the size of the new entry is larger than the table's capacity,
        /// the dynamic table will be cleared.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        private void Add(byte[] name, byte[] value)
        {
            int headerSize = HeaderField.SizeOf(name, value);

            // Clear the table if the header field size is larger than the capacity.
            if (headerSize > this.capacity) {
                this.Clear();
                return;
            }

            // Evict oldest entries until we have enough capacity.
            while(this.size + headerSize > this.capacity) {
                this.Remove();
            }

            // Copy name and value that modifications of original do not affect the dynamic table.
            name.CopyTo(name, 0);
            value.CopyTo(value, 0);

            int h = Encoder.Hash(name);
            int i = Encoder.Index(h);
            HeaderEntry old = headerFields[i];
            HeaderEntry e = new HeaderEntry(h, name, value, head.Before.Index - 1, old);
            headerFields[i] = e;
            e.AddBefore(head);
            this.size += headerSize;
        }

        /// <summary>
        /// Remove and return the oldest header field from the dynamic table.
        /// </summary>
        private HeaderField Remove()
        {
            if (this.size == 0) {
                return null;
            }
            HeaderEntry eldest = head.After;
            int h = eldest.Hash;
            int i = Encoder.Index(h);
            HeaderEntry prev = headerFields[i];
            HeaderEntry e = prev;
            while(e != null) {
                HeaderEntry next = e.Next;
                if (e == eldest) {
                    if (prev == eldest) {
                        headerFields[i] = next;
                    } else {
                        prev.Next = next;
                    }
                    eldest.Remove();
                    this.size -= eldest.Size;
                    return eldest;
                }
                prev = e;
                e = next;
            }
            return null;
        }

        /// <summary>
        /// Remove all entries from the dynamic table.
        /// </summary>
        private void Clear()
        {
            for(int i = 0; i < headerFields.Length; i++) {
                headerFields[i] = null;
            }
            head.Before = head.After = head;
            this.size = 0;
        }

        /// <summary>
        /// Returns the hash code for the given header field name.
        /// </summary>
        /// <returns><c>true</c> if hash name; otherwise, <c>false</c>.</returns>
        /// <param name="name">Name.</param>
        private static int Hash(byte[] name)
        {
            int h = 0;
            for(int i = 0; i < name.Length; i++) {
                h = 31 * h + name[i];
            }
            if (h > 0) {
                return h;
            } else if (h == int.MinValue) {
                return int.MaxValue;
            } else {
                return -h;
            }
        }

        /// <summary>
        /// Returns the index into the hash table for the hash code h.
        /// </summary>
        /// <param name="h">The height.</param>
        private static int Index(int h)
        {
            return h % BUCKET_SIZE;
        }

        /// <summary>
        /// A linked hash map HeaderField entry.
        /// </summary>
        private class HeaderEntry : HeaderField
        {
            // These fields comprise the doubly linked list used for iteration.
            private HeaderEntry before, after;

            // These fields comprise the chained list for header fields with the same hash.
            private HeaderEntry next;
            private int hash;

            // This is used to compute the index in the dynamic table.
            private int index;

            public HeaderEntry Before { get { return this.before; } set { this.before = value; } }

            public HeaderEntry After { get { return this.after; } set { this.after = value; } }

            public HeaderEntry Next { get { return this.next; } set { this.next = value; } }

            public int Hash { get { return this.hash; } }

            public int Index { get { return this.index; } }

            /// <summary>
            /// Creates new entry.
            /// </summary>
            /// <param name="hash">Hash.</param>
            /// <param name="name">Name.</param>
            /// <param name="value">Value.</param>
            /// <param name="index">Index.</param>
            /// <param name="next">Next.</param>
            public HeaderEntry(int hash, byte[] name, byte[] value, int index, HeaderEntry next) : base(name, value)
            {
                this.index = index;
                this.hash = hash;
                this.next = next;
            }

            /// <summary>
            /// Removes this entry from the linked list.
            /// </summary>
            public void Remove()
            {
                before.after = after;
                after.before = before;
            }

            /// <summary>
            /// Inserts this entry before the specified existing entry in the list.
            /// </summary>
            /// <param name="existingEntry">Existing entry.</param>
            public void AddBefore(HeaderEntry existingEntry)
            {
                after = existingEntry;
                before = existingEntry.before;
                before.after = this;
                after.before = this;
            }
        }
    }
}