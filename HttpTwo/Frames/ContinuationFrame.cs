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

    public class ContinuationFrame : Frame
    {
        public ContinuationFrame () : base ()
        {
            Headers = new NameValueCollection ();
        }

        public ContinuationFrame (uint streamIdentifier) : base ()
        {
            Headers = new NameValueCollection ();
            StreamIdentifier = streamIdentifier;
        }

        public bool EndHeaders { get; set; }

        public NameValueCollection Headers { get;set; }

        // type=0x1
        public override FrameType Type {
            get { return FrameType.Continuation; }
        }

        public override byte Flags {
            get { return EndHeaders ? (byte)0x4 : (byte)0x0; }
        }

        public override IEnumerable<byte> Payload {
            get {
                var data = new List<byte> ();

                // Header Block Fragments
                var hpackEncoder = new HPack.Encoder (4096);


                using (var ms = new MemoryStream ()) {
                    using (var bw = new BinaryWriter (ms)) {

                        foreach (var key in Headers.AllKeys) {
                            var values = Headers.GetValues (key);

                            foreach (var value in values)
                                hpackEncoder.EncodeHeader (bw, key, value, false);                        
                        }
                    }

                    var headerData = ms.ToArray ();
                    data.AddRange (headerData);
                }

                return data.ToArray ();
            }
        }

        public override void ParsePayload (byte[] payloadData, FrameHeader frameHeader)
        {
            EndHeaders = (frameHeader.Flags & 0x4) == 0x4;
         
            // Decode Header Block Fragments
            var hpackDecoder = new HPack.Decoder (8192, 4096);
            using(var binReader = new BinaryReader (new MemoryStream (payloadData))) {

                hpackDecoder.Decode(binReader, (name, value, sensitive) => 
                    Headers.Add (
                        System.Text.Encoding.ASCII.GetString (name), 
                        System.Text.Encoding.ASCII.GetString (value)));

                hpackDecoder.EndHeaderBlock(); // this must be called to finalize the decoding process.
            }
        }

        public override string ToString ()
        {
            var h = String.Join (", ", Headers.AllKeys.Select (n => n + "=" + Headers[n]));
            
            return string.Format ("[Frame: CONTINUATION, Id={0}, EndStream={1}, EndHeaders={2}, Headers=>{3}]", 
                StreamIdentifier, 
                IsEndStream, 
                EndHeaders, 
                h);
        }
    }
}
