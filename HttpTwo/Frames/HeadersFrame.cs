using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Specialized;
using System.Linq;

namespace HttpTwo
{
    public interface IFrameContainsHeaders : IFrame
    {
        byte[] HeaderBlockFragment { get; set; }
        bool EndHeaders { get;set; }
    }

    public class HeadersFrame : Frame, IFrameContainsHeaders
    {
        public HeadersFrame () : base ()
        {
        }

        public HeadersFrame (uint streamIdentifier) : base ()
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
        ushort weight = 0;
        public ushort Weight { 
            get { 
                return weight;
            }
            set {
                if (value > 255)
                    throw new ArgumentOutOfRangeException ("value", "Must be less than or equal to 255");
                weight = value;
            }
        }
        public bool Padded { get; set; }
        public bool EndStream { get; set; }
        public bool EndHeaders { get;set; }
        public bool Priority { get;set; }

        public byte[] HeaderBlockFragment { get; set; }

        public uint StreamDependency { get; set; } = 0;

        // type=0x1
        public override FrameType Type {
            get { return FrameType.Headers; }
        }

        public override byte Flags {
            get { 
                byte endStream = EndStream ? (byte)0x1 : (byte)0x0;
                byte padded = Padded ? (byte)0x8 : (byte)0x0;
                byte endHeaders = EndHeaders ? (byte)0x4 : (byte)0x0;
                byte priority = Priority ? (byte)0x20 : (byte)0x0;

                return (byte)(endStream | padded | endHeaders | priority);
            }
        }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                if (Padded) {
                    // Add the padding length 
                    data.Add ((byte)padLength);
                }

                if (Priority) {
                    // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
                    data.AddRange (Util.ConvertToUInt31 (StreamDependency).EnsureBigEndian ());

                    // Weight
                    var w = Priority ? weight : 0;
                    data.Add ((byte)w);
                }

                // Header Block Fragments
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
            EndStream = (frameHeader.Flags & 0x1) == 0x1;
            EndHeaders = (frameHeader.Flags & 0x4) == 0x4;
            Priority = (frameHeader.Flags & 0x20) == 0x20;
            Padded = (frameHeader.Flags & 0x8) == 0x8;

            var index = 0;

            if (Padded) {
                // Get pad length (1 byte)
                padLength = (ushort)payloadData [index];
                index++;
            } else {
                padLength = 0;
            }

            if (Priority) {
                // Get Dependency Stream Id
                // we need to turn the stream id into a uint
                var frameStreamIdData = new byte[4]; 
                Array.Copy (payloadData, index, frameStreamIdData, 0, 4);
                StreamDependency = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

                // Get the weight
                weight = (ushort)payloadData [index + 4];

                // Advance the index
                index += 5;
            }


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
            return string.Format ("[Frame: HEADERS, Id={0}, EndStream={1}, EndHeaders={2}, Priority={3}, Weight={4}, Padded={5}, PadLength={6}, HeaderBlockFragmentLength={7}]", 
                StreamIdentifier, 
                IsEndStream,
                EndHeaders,
                Priority,
                Weight,
                Padded,
                PadLength,
                HeaderBlockFragment.Length);
        }
    }
}
