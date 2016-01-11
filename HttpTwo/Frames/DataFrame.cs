using System;
using System.Collections.Generic;

namespace HttpTwo
{
    public class DataFrame : Frame
    {
        public DataFrame() : base ()
        {
        }
        
        public DataFrame (uint streamIdentifier) : base ()
        {
            StreamIdentifier = streamIdentifier;
        }

        uint padLength = 0;
        public uint PadLength { 
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
        public bool EndStream { get; set; }

        // type=0x0
        public override FrameType Type {
            get { return FrameType.Data; }
        }

        public override byte Flags {
            get { 
                byte endStream = EndStream ? (byte)0x1 : (byte)0x0;
                byte padded = Padded ? (byte)0x8 : (byte)0x0;

                return (byte)(endStream | padded);
            }
        }

        public byte[] Data { get; set; }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                // Add the padding length - optional
                if (Padded && padLength > 0)                    
                    data.Add ((byte)padLength);

                // Add the frame data
                if (Data != null)
                    data.AddRange (Data);

                // Add our padding
                for (int i = 0; i < padLength; i++)
                    data.Add (0x0);              

                return data;
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            EndStream = (frameHeader.Flags & 0x1) == 0x1;
            Padded = (frameHeader.Flags & 0x8) == 0x8;

            var index = 0;

            if (Padded) {
                padLength = (ushort)payloadData [index];
                index++;
            }

            // Data will be length of total payload - pad length value - the actual padding
            Data = new byte[payloadData.Length - (index + padLength)];
            Array.Copy (payloadData, index, Data, 0, Data.Length);
        }

        public override string ToString ()
        {
            return string.Format ("[Frame: DATA, Id={0}, EndStream={1}, Padded={2}, PadLength={3}, PayloadLength={4}]", 
                StreamIdentifier,
                IsEndStream,
                Padded,
                PadLength,
                PayloadLength);
        }
    }
    
}
