using System;
using System.Collections.Generic;
using HttpTwo.Internal;

namespace HttpTwo
{
    public class PriorityFrame : Frame
    {
        public PriorityFrame () : base ()
        {
        }

        public PriorityFrame (uint streamIdentifier) : base ()
        {
            StreamIdentifier = streamIdentifier;
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

        public uint StreamDependency { get; set; } = 0;

        // type=0x1
        public override FrameType Type {
            get { return FrameType.Priority; }
        }

        public override byte Flags {
            get { return (byte)0x0; }
        }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
                data.AddRange (Util.ConvertToUInt31 (StreamDependency).EnsureBigEndian ());
                data.Add ((byte)Weight);

                return data.ToArray ();
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            // Get Dependency Stream Id
            // we need to turn the stream id into a uint
            var frameStreamIdData = new byte[4]; 
            Array.Copy (payloadData, 0, frameStreamIdData, 0, 4);
            StreamDependency = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

            // Get the weight
            weight = (ushort)payloadData [4];
        }

        public override string ToString ()
        {
            return string.Format ("[Frame: PRIORITY, Id={0}, StreamDependency={1}, Weight={2}]", 
                StreamIdentifier,
                StreamDependency,
                Weight);
        }
    }
}
