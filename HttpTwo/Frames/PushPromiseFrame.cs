using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Specialized;

namespace HttpTwo
{

    public class PushPromiseFrame : Frame
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

        public NameValueCollection Headers { get;set; }

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
            var headerData = new byte[payloadData.Length - (index + padLength)];
            Array.Copy (payloadData, index, headerData, 0, headerData.Length);

            // Decode Header Block Fragments
            var hpackDecoder = new HPack.Decoder (8192, 4096);
            using(var binReader = new BinaryReader (new MemoryStream (headerData))) {

                hpackDecoder.Decode(binReader, (name, value, sensitive) => 
                    Headers.Add (
                        System.Text.Encoding.ASCII.GetString (name), 
                        System.Text.Encoding.ASCII.GetString (value)));

                hpackDecoder.EndHeaderBlock(); // this must be called to finalize the decoding process.
            }

            // Advance the index
            index += headerData.Length;

            // Don't care about padding
        }
    }
}
