using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using hpack;

namespace HttpTwo.Internal
{
    public static class Util
    {
        public static byte ClearBit (byte target, int bitIndex)
        {
            int x = Convert.ToInt32(target);
            x &= ~(1 << bitIndex);
            return Convert.ToByte(x);
        }

        public static bool IsBitSet (byte target, int bitIndex)
        {
            return (target & (1 << bitIndex)) != 0;
        }

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

        public static byte[] PackHeaders(Encoder hpackEncoder, NameValueCollection headers)
        {
            byte[] headerData = new byte[0];

            // Header Block Fragments

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

        public static NameValueCollection UnpackHeaders(Decoder hpackDecoder, byte[] data)
        {
            var headerListener = new HeaderListener();

            // Decode Header Block Fragments
            using(var binReader = new BinaryReader (new MemoryStream (data))) {

                hpackDecoder.Decode(binReader, headerListener);

                hpackDecoder.EndHeaderBlock(); // this must be called to finalize the decoding process.
            }

            return headerListener.Collection;
        }
    }
    class HeaderListener: IHeaderListener
    {
        NameValueCollection m_collection;
        public HeaderListener()
        {
            m_collection = new NameValueCollection();
        }
        public void AddHeader(byte[] name, byte[] value, bool sensitive)
        {
            m_collection.Add(
                        System.Text.Encoding.UTF8.GetString(name),
                        System.Text.Encoding.UTF8.GetString(value));
        }

        public NameValueCollection Collection { get { return m_collection;  } }
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
