using System;
using System.Collections.Generic;

namespace HttpTwo
{
    public class WindowUpdateFrame : Frame
    {
        public uint WindowSizeIncrement { get;set; }

        public override FrameType Type {
            get { return FrameType.WindowUpdate; }
        }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
                data.AddRange (Util.ConvertToUInt31 (WindowSizeIncrement).EnsureBigEndian ());

                return data;               
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            // we need to turn the stream id into a uint
            var windowSizeIncrData = new byte[4]; 
            Array.Copy (payloadData, 0, windowSizeIncrData, 0, 4);
            WindowSizeIncrement = Util.ConvertFromUInt31 (windowSizeIncrData.EnsureBigEndian ());
        }
    }
}
