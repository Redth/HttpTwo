using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;

namespace HttpTwo.Internal
{
    public static class Util
    {
        public static byte ClearBit (byte target, int bitIndex)
        {
            var x = Convert.ToInt32(target);
            x &= ~(1 << bitIndex);
            return Convert.ToByte(x);
        }

        public static bool IsBitSet(byte target, int bitIndex) => (target & (1 << bitIndex)) != 0;

        public static byte[] ConvertToUInt31 (uint original)
        {
            // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
            var data = BitConverter.GetBytes (original);
            data [3] = Util.ClearBit (data [3], 7);

            return data;
        }

        public static uint ConvertFromUInt31 (byte[] data)
        {
            if (data.Length != 4)
                return 0;

            data[3] = Util.ClearBit (data[3], 7);

            return BitConverter.ToUInt32 (data, 0);
        }

        public static byte[] PackHeaders (NameValueCollection headers, uint maxHeaderTableSize)
        {
            var headerData = new byte[0];

            // Header Block Fragments
            var hpackEncoder = new HPack.Encoder ((int)maxHeaderTableSize);

            using (var ms = new MemoryStream ()) {
                using (var bw = new BinaryWriter (ms)) {

                    foreach (var key in headers.AllKeys) {
                        var values = headers.GetValues (key);

                        foreach (var value in values)
                            hpackEncoder.EncodeHeader (bw, key, value, false);
                    }
                }

                headerData = ms.ToArray ();
            }

            return headerData;
        }

        public static NameValueCollection UnpackHeaders (byte[] data, int maxHeaderSize, int maxHeaderTableSize)
        {
            var headers = new NameValueCollection ();

            // Decode Header Block Fragments
            var hpackDecoder = new HPack.Decoder (maxHeaderSize, maxHeaderTableSize);
            using(var binReader = new BinaryReader (new MemoryStream (data))) {

                hpackDecoder.Decode(binReader, (name, value, sensitive) =>
                    headers.Add (
                        System.Text.Encoding.ASCII.GetString (name),
                        System.Text.Encoding.ASCII.GetString (value)));

                hpackDecoder.EndHeaderBlock(); // this must be called to finalize the decoding process.
            }

            return headers;
        }
    }

    static class ByteArrayExtensions
    {
        public static byte[] EnsureBigEndian (this byte[] src)
        {
            if (BitConverter.IsLittleEndian)
                Array.Reverse (src);

            return src;
        }
    }

    static class TaskExceptions
    {
        public static void Forget (this Task task)
        {
            //var a = task.ConfigureAwait(false);
        }
    }
}
