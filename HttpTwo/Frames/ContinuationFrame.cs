using System.Collections.Generic;

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
        public override FrameType Type => FrameType.Continuation;

        public override byte Flags => EndHeaders ? (byte)0x4 : (byte)0x0;

        public override IEnumerable<byte> Payload => HeaderBlockFragment ?? new byte[0];

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            EndHeaders = (frameHeader.Flags & 0x4) == 0x4;

            HeaderBlockFragment = new byte[payloadData.Length];
            payloadData.CopyTo (HeaderBlockFragment, 0);
        }

        public override string ToString() => string.Format("[Frame: CONTINUATION, Id={0}, EndStream={1}, EndHeaders={2}, HeaderBlockFragmentLength={3}]",
                StreamIdentifier,
                IsEndStream,
                EndHeaders,
                HeaderBlockFragment.Length);
    }
}
