using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Specialized;
using System.Linq;

namespace HttpTwo
{

    public class PushPromiseFrame : Frame, IFrameContainsHeaders
    {
        public PushPromiseFrame () : base ()
        {            
        }

        public PushPromiseFrame (uint streamIdentifier) : base ()
        {
            StreamIdentifier = streamIdentifier;
        }

        ushort padLength = 0;
        public ushort PadLength { 
            get { 
                return padLength;
            }
            set {
                if (value > 255)
                    throw new ArgumentOutOfRangeException ("value", "Must be less than or equal to 255");
                padLength = value;
            }
        }

        public bool Padded { get; set; }
        public bool EndHeaders { get;set; }

        public byte[] HeaderBlockFragment { get;set; }

        public uint StreamDependency { get; set; } = 0;

        // type=0x1
        public override FrameType Type {
            get { return FrameType.PushPromise; }
        }

        public override byte Flags {
            get { 
                byte padded = Padded ? (byte)0x8 : (byte)0x0;
                byte endHeaders = EndHeaders ? (byte)0x4 : (byte)0x0;

                return (byte)(padded | endHeaders);
            }
        }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                if (Padded) {
                    // Add the padding length 
                    data.Add ((byte)padLength);
                }

                // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
                data.AddRange (Util.ConvertToUInt31 (StreamDependency).EnsureBigEndian ());

                if (HeaderBlockFragment != null && HeaderBlockFragment.Length > 0)
                    data.AddRange (HeaderBlockFragment);

                // Add our padding
                for (int i = 0; i < padLength; i++)
                    data.Add (0x0);              

                return data.ToArray ();
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            EndHeaders = (frameHeader.Flags & 0x4) == 0x4;
            Padded = (frameHeader.Flags & 0x8) == 0x8;

            var index = 0;

            if (Padded) {
                // Get pad length (1 byte)
                padLength = (ushort)payloadData [index];
                index++;
            } else {
                padLength = 0;
            }

            // Get Dependency Stream Id
            // we need to turn the stream id into a uint
            var frameStreamIdData = new byte[4]; 
            Array.Copy (payloadData, index, frameStreamIdData, 0, 4);
            StreamDependency = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

            // Advance the index
            index += 4;

            // create an array for the header data to read
            // it will be the payload length, minus the pad length value, weight, stream id, and padding
            HeaderBlockFragment = new byte[payloadData.Length - (index + padLength)];
            Array.Copy (payloadData, index, HeaderBlockFragment, 0, HeaderBlockFragment.Length);

            // Advance the index
            index += HeaderBlockFragment.Length;

            // Don't care about padding
        }

        public override string ToString ()
        {
            return string.Format ("[Frame: PUSH_PROMISE, Id={0}, EndStream={1}, EndHeaders={2}, StreamDependency={3}, Padded={4}, PadLength={5}, HeaderBlockFragmentLength={6}]", 
                StreamIdentifier, 
                IsEndStream,
                EndHeaders,
                StreamDependency,
                Padded,
                PadLength,
                HeaderBlockFragment.Length);
        }
    }
}
