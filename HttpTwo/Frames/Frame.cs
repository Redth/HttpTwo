using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using HttpTwo.Internal;

namespace HttpTwo
{
    public class FrameHeader
    {
        public const int FrameHeaderLength = 9;

        public uint Length { get;set; }
        public byte Type { get;set; }
        public byte Flags { get;set; }
        public uint StreamIdentifier { get;set; }
    }

    public interface IFrame
    {
        uint Length { get; }
        FrameType Type { get; }
        byte Flags { get; }
        uint StreamIdentifier { get; set; }
        IEnumerable<byte> Payload { get; }
        uint PayloadLength { get; }
        IEnumerable<byte> ToBytes ();
        bool IsEndStream { get; }
    }

    public abstract class Frame : IFrame
    {
        public static Frame Create (FrameType frameType)
        {
            switch (frameType) {
            case FrameType.Data:
                return new DataFrame ();
            case FrameType.Headers:
                return new HeadersFrame ();
            case FrameType.Priority:
                return new PriorityFrame ();
            case FrameType.RstStream:
                return new RstStreamFrame ();
            case FrameType.Settings:
                return new SettingsFrame ();
            case FrameType.PushPromise:
                return new PushPromiseFrame ();
            case FrameType.Ping:
                return new PingFrame ();
            case FrameType.GoAway:
                return new GoAwayFrame ();
            case FrameType.WindowUpdate:
                return new WindowUpdateFrame ();
            case FrameType.Continuation:
                return new ContinuationFrame ();
            }

            return null;
        }

        protected Frame() => Flags = (byte)0;

        public uint Length => PayloadLength;

        public abstract FrameType Type { get; }

        public virtual byte Flags { get; protected set; } = 0x0;

        public virtual uint StreamIdentifier { get; set; }

        public virtual IEnumerable<byte> Payload { get; }

        uint? payloadLength;
        public uint PayloadLength {
            get {
                if (!payloadLength.HasValue)
                    payloadLength = (uint)Payload.Count ();

                return payloadLength.Value;
            }
        }

        public bool IsEndStream => (Type == FrameType.Data || Type == FrameType.Headers)
                        && (Flags & 0x1) == 0x1;

        byte[] To24BitInt (uint original)
        {
            var b = BitConverter.GetBytes (original);

            return new [] {
                b[0], b[1], b[2]
            };
        }

        public IEnumerable<byte> ToBytes ()
        {
            var data = new List<byte> ();

            // Copy Frame Length
            var frameLength = To24BitInt (Length);
            data.AddRange (frameLength.EnsureBigEndian ());

            // Copy Type
            data.Add ((byte)Type);

            // Copy Flags
            data.Add (Flags);

            // 1 Bit reserved as unset (0) so let's take the first bit of the next 32 bits and unset it
            var streamId = Util.ConvertToUInt31 (StreamIdentifier);
            data.AddRange (streamId.EnsureBigEndian ());

            var payloadData = Payload.ToArray ();
            // Now the payload
            data.AddRange (payloadData);

            return data;
        }

        internal void Parse (byte[] data)
        {
            if (data.Length < 9)
                throw new InvalidDataException ("data[] is missing frame header");

            // Find out the frame length
            // which is a 24 bit uint, so we need to convert this as c# uint is 32 bit
            var flen = new byte[4];
            flen [0] = 0x0;
            flen [1] = data [0];
            flen [2] = data [1];
            flen [3] = data [2];

            var frameLength = BitConverter.ToUInt32 (flen.EnsureBigEndian (), 0);

            // If we are expecting a payload that's bigger than what's in our buffer
            // we should keep reading from the stream
            if (data.Length - 9 < frameLength)
                throw new InvalidDataException ("Length of data[] does not match frame length in data");

            var frameType = data [3]; // 4th byte in frame header is TYPE
            var frameFlags = data [4]; // 5th byte is FLAGS

            // we need to turn the stream id into a uint
            var frameStreamIdData = new byte[4];
            Array.Copy (data, 5, frameStreamIdData, 0, 4);
            this.StreamIdentifier = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

            //this.Type = frameType;
            this.Flags = frameFlags;

            var frameHeader = new FrameHeader {
                Length = frameLength,
                Type = frameType,
                Flags = frameFlags,
                StreamIdentifier = this.StreamIdentifier
            };

            // Isolate the payload data
            var payloadData = new byte[frameLength];
            Array.Copy (data, 9, payloadData, 0, frameLength);

            ParsePayload (payloadData, frameHeader);
        }

        public abstract void ParsePayload (byte[] payloadData, FrameHeader frameHeader);

        public override string ToString() => string.Format("[Frame: {0}, Id={1}, Flags={2}, PayloadLength={3}, IsEndStream={4}]", Type.ToString().ToUpperInvariant(), StreamIdentifier, Flags, PayloadLength, IsEndStream);
    }

    public enum FrameType
    {
        Data = 0x0,
        Headers = 0x1,
        Priority = 0x2,
        RstStream = 0x3,
        Settings = 0x4,
        PushPromise = 0x5,
        Ping = 0x6,
        GoAway = 0x7,
        WindowUpdate = 0x8,
        Continuation = 0x9
    }
}
