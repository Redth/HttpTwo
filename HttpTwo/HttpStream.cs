using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace HttpTwo
{
    public class HttpStream
    {        
        public HttpStream (Http2Connection connection)
        {            
            Init (connection, connection.GetNextId ());
        }

        public HttpStream (Http2Connection connection, uint streamIdentifier)
        {
            Init (connection, streamIdentifier);
        }

        void Init (Http2Connection connection, uint streamIdentifier)
        {
            Connection = connection;
            Frames = new List<Frame> ();
            SentFrames = new List<Frame> ();
            StreamIdentifer = streamIdentifier;
            State = StreamState.Idle;
        }

        public Http2Connection Connection { get; private set; }

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
