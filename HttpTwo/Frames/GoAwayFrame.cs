using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections.Specialized;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;

namespace HttpTwo
{

    public class GoAwayFrame : Frame
    {
        public uint LastStreamId { get;set; }
        public uint ErrorCode { get;set; }
        public byte[] AdditionalDebugData { get; set; }

        // type=0x7
        public override FrameType Type {
            get { return FrameType.GoAway; }
        }
            
        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
                data.AddRange (Util.ConvertToUInt31 (LastStreamId).EnsureBigEndian ());

                data.AddRange (BitConverter.GetBytes (ErrorCode).EnsureBigEndian ());

                if (AdditionalDebugData != null && AdditionalDebugData.Length > 0)
                    data.AddRange (AdditionalDebugData);

                return data;               
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            // we need to turn the stream id into a uint
            var frameStreamIdData = new byte[4]; 
            Array.Copy (payloadData, 0, frameStreamIdData, 0, 4);
            LastStreamId = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

            var errorCodeData = new byte[4];
            Array.Copy (payloadData, 4, errorCodeData, 0, 4);
            uint errorCode = BitConverter.ToUInt32 (errorCodeData.EnsureBigEndian (), 0);
            ErrorCode = errorCode;

            if (payloadData.Length > 8)
            {
                AdditionalDebugData = new byte[payloadData.Length - 8];
                Array.Copy(payloadData, 8, AdditionalDebugData, 0, payloadData.Length - 8);
            }
        }
    }
    
}
