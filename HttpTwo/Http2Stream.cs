using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;

namespace HttpTwo
{
    public class Http2Stream
    {        
        public Http2Stream (IFlowControlManager flowControlStateManager, uint streamIdentifier)
        {
            this.flowControlStateManager = flowControlStateManager;

            ReceivedFrames = new List<IFrame> ();
            SentFrames = new List<IFrame> ();
            StreamIdentifer = streamIdentifier;
            State = StreamState.Idle;
        }

        public uint StreamIdentifer { get; private set; }
        public StreamState State { get; private set; }

        public List<IFrame> ReceivedFrames { get; private set; }
        public List<IFrame> SentFrames { get; private set; }

        IFlowControlManager flowControlStateManager;

        public void ProcessReceivedFrames (IFrame frame)
        {   
            // Add frame to the list of history
            ReceivedFrames.Add (frame);

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

            // Server has cleared up more window space
            // Add more to the available window
            if (frame.Type == FrameType.WindowUpdate) {
                var windowUpdateFrame = (WindowUpdateFrame)frame;
                flowControlStateManager.IncreaseWindowSize (StreamIdentifer, windowUpdateFrame.WindowSizeIncrement);
            }

            // Raise the event
            OnFrameReceived?.Invoke (frame);
        }

        public void ProcessSentFrame (IFrame frame)
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

            // If data frame, decrease available window
            if (frame.Type == FrameType.Data) {
                var windowDecrement = ((DataFrame)frame).PayloadLength;
                flowControlStateManager.DecreaseWindowSize (frame.StreamIdentifier, (uint)windowDecrement);
            }

            // Raise the event
            OnFrameSent?.Invoke (frame);
        }
        
        public delegate void FrameReceivedDelegate (IFrame frame);
        public event FrameReceivedDelegate OnFrameReceived;

        public delegate void FrameSentDelegate (IFrame frame);
        public event FrameSentDelegate OnFrameSent;
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
