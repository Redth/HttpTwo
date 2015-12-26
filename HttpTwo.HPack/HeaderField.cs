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
using System.Text;

namespace HttpTwo.HPack
{
    public class HeaderField : IComparable<HeaderField>
    {
        private byte[] name;
        private byte[] value;

        // Section 4.1. Calculating Table Size
        // The additional 32 octets account for an estimated
        // overhead associated with the structure.
        public static int HEADER_ENTRY_OVERHEAD = 32;

        public byte[] Name { get { return this.name; } }

        public byte[] Value { get { return this.value; } }

        public int Size { get { return this.name.Length + this.value.Length + HEADER_ENTRY_OVERHEAD; } }

        // This constructor can only be used if name and value are ISO-8859-1 encoded.
        public HeaderField(string name, string value)
        {
            this.name = Encoding.UTF8.GetBytes(name);
            this.value = Encoding.UTF8.GetBytes(value);
        }

        public HeaderField(byte[] name, byte[] value)
        {
            this.name = (byte[])HPackUtil.RequireNonNull(name);
            this.value = (byte[])HPackUtil.RequireNonNull(value);
        }

        public static int SizeOf(byte[] name, byte[] value)
        {
            return name.Length + value.Length + HEADER_ENTRY_OVERHEAD;
        }

        public int CompareTo(HeaderField anotherHeaderField)
        {
            int ret = this.CompareTo(name, anotherHeaderField.name);
            if (ret == 0) {
                ret = this.CompareTo(value, anotherHeaderField.value);
            }
            return ret;
        }

        private int CompareTo(byte[] s1, byte[] s2)
        {
            int len1 = s1.Length;
            int len2 = s2.Length;
            int lim = Math.Min(len1, len2);

            int k = 0;
            while(k < lim) {
                byte b1 = s1[k];
                byte b2 = s2[k];
                if (b1 != b2) {
                    return b1 - b2;
                }
                k++;
            }
            return len1 - len2;
        }

        public override bool Equals(Object obj)
        {
            if (obj == this) {
                return true;
            }
            if (!(obj is HeaderField)) {
                return false;
            }
            HeaderField other = (HeaderField)obj;
            bool nameEquals = HPackUtil.Equals(name, other.name);
            bool valueEquals = HPackUtil.Equals(value, other.value);
            return nameEquals && valueEquals;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override String ToString()
        {
            return String.Format("{0}: {1}", Encoding.UTF8.GetString(this.name), Encoding.UTF8.GetString(this.value));
        }
    }
}