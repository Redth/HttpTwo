using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections.Specialized;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Net;

namespace HttpTwo
{

    public class ContinuationFrame : Frame, IFrameContainsHeaders
    {
        public ContinuationFrame () : base ()
        {
        }

        public ContinuationFrame (uint streamIdentifier) : base ()
        {
            StreamIdentifier = streamIdentifier;
        }

        public bool EndHeaders { get; set; }

        public byte[] HeaderBlockFragment { get;set; }

        // type=0x1
        public override FrameType Type {
            get { return FrameType.Continuation; }
        }

        public override byte Flags {
            get { return EndHeaders ? (byte)0x4 : (byte)0x0; }
        }

        public override IEnumerable<byte> Payload {
            get {
                return HeaderBlockFragment ?? new byte[0];
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            EndHeaders = (frameHeader.Flags & 0x4) == 0x4;

            HeaderBlockFragment = new byte[payloadData.Length];
            payloadData.CopyTo (HeaderBlockFragment, 0);
        }

        public override string ToString ()
        {
            return string.Format ("[Frame: CONTINUATION, Id={0}, EndStream={1}, EndHeaders={2}, HeaderBlockFragmentLength={3}]", 
                StreamIdentifier, 
                IsEndStream, 
                EndHeaders, 
                HeaderBlockFragment.Length);
        }
    }
}
