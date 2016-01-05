using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace HttpTwo
{
    public class HttpStream
    {
        const uint STREAM_ID_MAX_VALUE = 1073741823;

        static uint streamId = 1;

        internal static uint GetNextId ()
        {
            var nextId = streamId;

            // Increment for next use, by 2, must always be odd if initiated from client
            streamId += 2;

            // Wrap around if we hit max
            if (streamId > STREAM_ID_MAX_VALUE)
                streamId = 1;

            return nextId;
        }
        
        public HttpStream ()            
        {            
            Init (GetNextId ());
        }

        public HttpStream (uint streamIdentifier)
        {
            Init (streamIdentifier);
        }

        void Init (uint streamIdentifier)
        {
            Frames = new List<Frame> ();
            SentFrames = new List<Frame> ();
            StreamIdentifer = streamIdentifier;
            State = StreamState.Idle;
        }

        public uint StreamIdentifer { get; private set; }

        public StreamState State { get; set; }

        public List<Frame> Frames { get;set; }

        public List<Frame> SentFrames { get;set; }


        public void ProcessFrame (Frame frame)
        {   
            // Add frame to the list of history
            Frames.Add (frame);

            if (State == StreamState.Idle) {
                if (frame.Type == FrameType.Headers)
                    State = StreamState.Open;
                else if (frame.Type == FrameType.PushPromise)
                    State = StreamState.ReservedRemote;
                else if (frame.Type == FrameType.Priority)
                    ;
                else
                    ; // TODO: PROTOCOL_ERROR
            } else if (State == StreamState.ReservedLocal) {
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.HalfClosedRemote) {
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.Open) {
                if (frame.IsEndStream)
                    State = StreamState.HalfClosedRemote;
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.ReservedRemote) {
                if (frame.Type == FrameType.Headers)
                    State = StreamState.HalfClosedLocal;
                else if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.HalfClosedLocal) {
                if (frame.IsEndStream || frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            }

            // Raise the event
            OnFrameReceived?.Invoke (frame);
        }

        public void ProcessSentFrame (Frame frame)
        {
            SentFrames.Add (frame);

            if (State == StreamState.Idle) {
                if (frame.Type == FrameType.PushPromise)
                    State = StreamState.ReservedLocal;
                else if (frame.Type == FrameType.Headers)
                    State = StreamState.Open;
            } else if (State == StreamState.ReservedLocal) {
                if (frame.Type == FrameType.Headers)
                    State = StreamState.HalfClosedRemote;
                else if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.HalfClosedRemote) {
                if (frame.IsEndStream || frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;                
            } else if (State == StreamState.Open) {
                if (frame.IsEndStream)
                    State = StreamState.HalfClosedLocal;
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.ReservedRemote) {
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            } else if (State == StreamState.HalfClosedLocal) {
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
            }
        }
        
        public delegate void FrameReceivedDelegate (Frame frame);
        public event FrameReceivedDelegate OnFrameReceived;
    }

    public enum StreamState {
        Idle,
        ReservedLocal,
        ReservedRemote,
        Open,
        HalfClosedLocal,
        HalfClosedRemote,
        Closed
    }
}
