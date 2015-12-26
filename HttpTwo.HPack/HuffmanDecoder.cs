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
    public class HuffmanDecoder
    {
        private static IOException EOS_DECODED = new IOException("EOS Decoded");
        private static IOException INVALID_PADDING = new IOException("Invalid Padding");

        private Node root;

        /// <summary>
        /// Creates a new Huffman decoder with the specified Huffman coding.
        /// </summary>
        /// <param name="codes">the Huffman codes indexed by symbol</param>
        /// <param name="lengths">the length of each Huffman code</param>
        public HuffmanDecoder(int[] codes, byte[] lengths)
        {
            if (codes.Length != 257 || codes.Length != lengths.Length) {
                throw new ArgumentException("invalid Huffman coding");
            }
            this.root = BuildTree(codes, lengths);
        }

        /// <summary>
        /// Decompresses the given Huffman coded string literal.
        /// </summary>
        /// <param name="buf">the string literal to be decoded</param>
        /// <returns>the output stream for the compressed data</returns>
        /// <exception cref="IOException">throws IOException if an I/O error occurs. In particular, an <code>IOException</code> may be thrown if the output stream has been closed.</exception>
        public byte[] Decode(byte[] buf)
        {
            using(var baos = new MemoryStream()) {
                Node node = root;
                int current = 0;
                int bits = 0;
                for(int i = 0; i < buf.Length; i++) {
                    int b = buf[i] & 0xFF;
                    current = (current << 8) | b;
                    bits += 8;
                    while(bits >= 8) {
                        int c = (current >> (bits - 8)) & 0xFF;
                        node = node.Children[c];
                        bits -= node.Bits;
                        if (node.IsTerminal()) {
                            if (node.Symbol == HPackUtil.HUFFMAN_EOS) {
                                throw EOS_DECODED;
                            }
                            baos.Write(new byte[] { (byte)node.Symbol }, 0, 1);
                            node = root;
                        }
                    }
                }

                while(bits > 0) {
                    int c = (current << (8 - bits)) & 0xFF;
                    node = node.Children[c];
                    if (node.IsTerminal() && node.Bits <= bits) {
                        bits -= node.Bits;
                        baos.Write(new byte[] { (byte)node.Symbol }, 0, 1);
                        node = root;
                    } else {
                        break;
                    }
                }

                // Section 5.2. String Literal Representation
                // Padding not corresponding to the most significant bits of the code
                // for the EOS symbol (0xFF) MUST be treated as a decoding error.
                int mask = (1 << bits) - 1;
                if ((current & mask) != mask) {
                    throw INVALID_PADDING;
                }

                return baos.ToArray();
            }
        }

        public class Node
        {
            private int symbol;
            // terminal nodes have a symbol
            private int bits;
            // number of bits matched by the node
            private Node[] children;
            // internal nodes have children

            public int Symbol { get { return this.symbol; } }

            public int Bits { get { return this.bits; } }

            public Node[] Children { get { return this.children; } }

            /// <summary>
            /// Initializes a new instance of the <see cref="hpack.HuffmanDecoder+Node"/> class.
            /// </summary>
            public Node()
            {
                symbol = 0;
                bits = 8;
                children = new Node[256];
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="hpack.HuffmanDecoder+Node"/> class.
            /// </summary>
            /// <param name="symbol">the symbol the node represents</param>
            /// <param name="bits">the number of bits matched by this node</param>
            public Node(int symbol, int bits)
            {
                //assert(bits > 0 && bits <= 8);
                this.symbol = symbol;
                this.bits = bits;
                children = null;
            }

            public bool IsTerminal()
            {
                return children == null;
            }
        }

        private static Node BuildTree(int[] codes, byte[] lengths)
        {
            Node root = new Node();
            for(int i = 0; i < codes.Length; i++) {
                Insert(root, i, codes[i], lengths[i]);
            }
            return root;
        }

        private static void Insert(Node root, int symbol, int code, byte length)
        {
            // traverse tree using the most significant bytes of code
            Node current = root;
            while(length > 8) {
                if (current.IsTerminal()) {
                    throw new InvalidDataException("invalid Huffman code: prefix not unique");
                }
                length -= 8;
                int i = (code >> length) & 0xFF;
                if (current.Children[i] == null) {
                    current.Children[i] = new Node();
                }
                current = current.Children[i];
            }

            Node terminal = new Node(symbol, length);
            int shift = 8 - length;
            int start = (code << shift) & 0xFF;
            int end = 1 << shift;
            for(int i = start; i < start + end; i++) {
                current.Children[i] = terminal;
            }
        }
    }
}