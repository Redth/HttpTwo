using System;
using System.Collections.Generic;
using HttpTwo.Internal;

namespace HttpTwo
{
    public class GoAwayFrame : Frame
    {
        public uint LastStreamId { get;set; }
        public uint ErrorCode { get;set; }
        public byte[] AdditionalDebugData { get; set; }

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

        public override string ToString ()
        {
            var debug = string.Empty;
            if (AdditionalDebugData != null && AdditionalDebugData.Length > 0)
                debug = System.Text.Encoding.ASCII.GetString (AdditionalDebugData);
            
            return string.Format ("[Frame: GOAWAY, Id={0}, ErrorCode={1}, LastStreamId={2}, AdditionalDebugData={3}]", 
                StreamIdentifier, 
                ErrorCode, 
                LastStreamId, 
                debug);
        }
    }
}
