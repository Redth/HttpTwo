using System;
using System.Net;

namespace HttpTwo
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
}

