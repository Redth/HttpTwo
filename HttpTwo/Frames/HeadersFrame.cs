using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Specialized;
using System.Linq;

namespace HttpTwo
{

    public class HeadersFrame : Frame
    {
        public HeadersFrame () : base ()
        {
            Headers = new NameValueCollection ();
        }

        public HeadersFrame (uint streamIdentifier) : base ()
        {
            Headers = new NameValueCollection ();
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

        public NameValueCollection Headers { get;set; }

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

//                    var hpackDecoder = new HPack.Decoder (8192, 4096);
//                    using(var binReader = new BinaryReader (new MemoryStream (headerData))) {
//
//                        hpackDecoder.Decode(binReader, (name, value, sensitive) => 
//                            Console.WriteLine ("{0} = {1}", System.Text.Encoding.ASCII.GetString (name), System.Text.Encoding.ASCII.GetString (value)));
//
//                        hpackDecoder.EndHeaderBlock(); // this must be called to finalize the decoding process.
//                    }

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

        public override string ToString ()
        {
            var h = String.Join (", ", Headers.AllKeys.Select (n => n + "=" + Headers[n]));

            return string.Format ("[Frame: HEADERS, Id={0}, EndStream={1}, EndHeaders={2}, Priority={3}, Weight={4}, Padded={5}, PadLength={6}, Headers={7}]", 
                StreamIdentifier, 
                IsEndStream,
                EndHeaders,
                Priority,
                Weight,
                Padded,
                PadLength,
                h);
        }
    }
}
