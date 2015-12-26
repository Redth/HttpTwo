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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections;
using System.Text;

namespace HttpTwo.HPack
{
    public static class StaticTable
    {
        /// <summary>
        /// The static table
        /// Appendix A: Static Table
        /// </summary>
        /// <see cref="http://tools.ietf.org/html/rfc7541#appendix-A"/>
        private static List<HeaderField> STATIC_TABLE = new List<HeaderField>() {
            /*  1 */new HeaderField(":authority", String.Empty),
            /*  2 */new HeaderField(":method", "GET"),
            /*  3 */new HeaderField(":method", "POST"),
            /*  4 */new HeaderField(":path", "/"),
            /*  5 */new HeaderField(":path", "/index.html"),
            /*  6 */new HeaderField(":scheme", "http"),
            /*  7 */new HeaderField(":scheme", "https"),
            /*  8 */new HeaderField(":status", "200"),
            /*  9 */new HeaderField(":status", "204"),
            /* 10 */new HeaderField(":status", "206"),
            /* 11 */new HeaderField(":status", "304"),
            /* 12 */new HeaderField(":status", "400"),
            /* 13 */new HeaderField(":status", "404"),
            /* 14 */new HeaderField(":status", "500"),
            /* 15 */new HeaderField("accept-charset", String.Empty),
            /* 16 */new HeaderField("accept-encoding", "gzip, deflate"),
            /* 17 */new HeaderField("accept-language", String.Empty),
            /* 18 */new HeaderField("accept-ranges", String.Empty),
            /* 19 */new HeaderField("accept", String.Empty),
            /* 20 */new HeaderField("access-control-allow-origin", String.Empty),
            /* 21 */new HeaderField("age", String.Empty),
            /* 22 */new HeaderField("allow", String.Empty),
            /* 23 */new HeaderField("authorization", String.Empty),
            /* 24 */new HeaderField("cache-control", String.Empty),
            /* 25 */new HeaderField("content-disposition", String.Empty),
            /* 26 */new HeaderField("content-encoding", String.Empty),
            /* 27 */new HeaderField("content-language", String.Empty),
            /* 28 */new HeaderField("content-length", String.Empty),
            /* 29 */new HeaderField("content-location", String.Empty),
            /* 30 */new HeaderField("content-range", String.Empty),
            /* 31 */new HeaderField("content-type", String.Empty),
            /* 32 */new HeaderField("cookie", String.Empty),
            /* 33 */new HeaderField("date", String.Empty),
            /* 34 */new HeaderField("etag", String.Empty),
            /* 35 */new HeaderField("expect", String.Empty),
            /* 36 */new HeaderField("expires", String.Empty),
            /* 37 */new HeaderField("from", String.Empty),
            /* 38 */new HeaderField("host", String.Empty),
            /* 39 */new HeaderField("if-match", String.Empty),
            /* 40 */new HeaderField("if-modified-since", String.Empty),
            /* 41 */new HeaderField("if-none-match", String.Empty),
            /* 42 */new HeaderField("if-range", String.Empty),
            /* 43 */new HeaderField("if-unmodified-since", String.Empty),
            /* 44 */new HeaderField("last-modified", String.Empty),
            /* 45 */new HeaderField("link", String.Empty),
            /* 46 */new HeaderField("location", String.Empty),
            /* 47 */new HeaderField("max-forwards", String.Empty),
            /* 48 */new HeaderField("proxy-authenticate", String.Empty),
            /* 49 */new HeaderField("proxy-authorization", String.Empty),
            /* 50 */new HeaderField("range", String.Empty),
            /* 51 */new HeaderField("referer", String.Empty),
            /* 52 */new HeaderField("refresh", String.Empty),
            /* 53 */new HeaderField("retry-after", String.Empty),
            /* 54 */new HeaderField("server", String.Empty),
            /* 55 */new HeaderField("set-cookie", String.Empty),
            /* 56 */new HeaderField("strict-transport-security", String.Empty),
            /* 57 */new HeaderField("transfer-encoding", String.Empty),
            /* 58 */new HeaderField("user-agent", String.Empty),
            /* 59 */new HeaderField("vary", String.Empty),
            /* 60 */new HeaderField("via", String.Empty),
            /* 61 */new HeaderField("www-authenticate", String.Empty)
        };

        private static Dictionary<string, int> STATIC_INDEX_BY_NAME = CreateMap();

        /// <summary>
        /// The number of header fields in the static table.
        /// </summary>
        /// <value>The length.</value>
        public static int Length { get { return STATIC_TABLE.Count; } }

        /// <summary>
        /// Return the header field at the given index value.
        /// </summary>
        /// <returns>The entry.</returns>
        /// <param name="index">Index.</param>
        public static HeaderField GetEntry(int index)
        {
            return STATIC_TABLE[index - 1];
        }

        /// <summary>
        /// Returns the lowest index value for the given header field name in the static table.
        /// Returns -1 if the header field name is not in the static table.
        /// </summary>
        /// <returns>The index.</returns>
        /// <param name="name">Name.</param>
        public static int GetIndex(byte[] name)
        {
            string nameString = Encoding.UTF8.GetString(name);
            if (!STATIC_INDEX_BY_NAME.ContainsKey(nameString)) {
                return -1;
            }
            return STATIC_INDEX_BY_NAME[nameString];
        }

        /// <summary>
        /// Returns the index value for the given header field in the static table.
        /// Returns -1 if the header field is not in the static table.
        /// </summary>
        /// <returns>The index.</returns>
        /// <param name="name">Name.</param>
        /// <param name="value">Value.</param>
        public static int GetIndex(byte[] name, byte[] value)
        {
            int index = GetIndex(name);
            if (index == -1) {
                return -1;
            }

            // Note this assumes all entries for a given header field are sequential.
            while(index <= StaticTable.Length) {
                HeaderField entry = GetEntry(index);
                if (!HPackUtil.Equals(name, entry.Name)) {
                    break;
                }
                if (HPackUtil.Equals(value, entry.Value)) {
                    return index;
                }
                index++;
            }

            return -1;
        }

        /// <summary>
        /// create a map of header name to index value to allow quick lookup
        /// </summary>
        /// <returns>The map.</returns>
        private static Dictionary<string, int> CreateMap()
        {
            int length = STATIC_TABLE.Count;
            var ret = new Dictionary<string, int>(length);

            // Iterate through the static table in reverse order to
            // save the smallest index for a given name in the map.
            for(int index = length; index > 0; index--) {
                HeaderField entry = GetEntry(index);
                string name = Encoding.UTF8.GetString(entry.Name);
                ret[name] = index;
            }
            return ret;
        }
    }
}