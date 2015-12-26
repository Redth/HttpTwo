using System;
using System.Collections.Generic;

namespace HttpTwo
{

    public class SettingsFrame : Frame
    {
        public bool Ack { get; set; }

        // type=0x4
        public override FrameType Type {
            get { return FrameType.Settings; }
        }

        public override byte Flags {
            get { return Ack ? (byte)0x1 : (byte)0x0; }
        }

        public override uint StreamIdentifier {
            get { return 0x0; }
        }

        public uint? HeaderTableSize { get; set; } // 4096 is default (0x1)
        public uint? EnablePush { get;set; } // 1 or 0 (0x2)
        public uint? MaxConcurrentStreams { get;set; } // (0x3)
        public uint? InitialWindowSize { get;set; } // (0x4) 
        public uint? MaxFrameSize { get;set; } // (0x5)
        public uint? MaxHeaderListSize { get;set; } // (0x6) 

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                if (HeaderTableSize.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x1));
                    data.AddRange (BitConverter.GetBytes (HeaderTableSize.Value));
                }

                if (EnablePush.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x2));
                    data.AddRange (BitConverter.GetBytes (EnablePush.Value));
                }

                if (MaxConcurrentStreams.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x3));
                    data.AddRange (BitConverter.GetBytes (MaxConcurrentStreams.Value));
                }

                if (InitialWindowSize.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x4));
                    data.AddRange (BitConverter.GetBytes (InitialWindowSize.Value));
                }

                if (MaxFrameSize.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x5));
                    data.AddRange (BitConverter.GetBytes (MaxFrameSize.Value));
                }

                if (MaxHeaderListSize.HasValue) {
                    data.AddRange (BitConverter.GetBytes ((ushort)0x6));
                    data.AddRange (BitConverter.GetBytes (MaxHeaderListSize.Value));
                }

                return data;
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            Ack = (frameHeader.Flags & 0x1) == 0x1;

            for (int i = 0; i < payloadData.Length; i+=6) {

//                var idData = new byte[2];
//                Array.Copy (payloadData, i, idData, 0, 2);

                //var id = BitConverter.ToUInt16 (idData, 0);

                var value = BitConverter.ToUInt32 (payloadData, i + 2);

                switch (value) {
                case 0x1:
                    HeaderTableSize = value;
                    break;
                case 0x2:
                    EnablePush = value;
                    break;
                case 0x3:
                    MaxConcurrentStreams = value;
                    break;
                case 0x4:
                    InitialWindowSize = value;
                    break;
                case 0x5:
                    MaxFrameSize = value;
                    break;
                case 0x6:
                    MaxHeaderListSize = value;
                    break;
                }
            }
        }
    }
}
