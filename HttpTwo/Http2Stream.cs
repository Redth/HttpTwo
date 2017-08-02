﻿using System;
using System.Collections.Generic;
using HttpTwo.Internal;

namespace HttpTwo
{
    public class Http2Stream
    {        
        public Http2Stream (IFlowControlManager flowControlStateManager, uint streamIdentifier)
        {
            this.flowControlStateManager = flowControlStateManager;

            StreamIdentifer = streamIdentifier;
            State = StreamState.Idle;
        }

        public uint StreamIdentifer { get; private set; }
        public StreamState State { get; private set; }

        readonly IFlowControlManager flowControlStateManager;

        public void ProcessReceivedFrames (IFrame frame)
        {

            switch (State) {
            case StreamState.Idle:
                if (frame.Type == FrameType.Headers)
                    State = StreamState.Open;
                else if (frame.Type == FrameType.PushPromise)
                    State = StreamState.ReservedRemote;
//                else if (frame.Type == FrameType.Priority)
//                    ;
//                else
//                    ;
                break;
            case StreamState.ReservedLocal:
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.HalfClosedRemote:
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.Open:
                if (frame.IsEndStream)
                    State = StreamState.HalfClosedRemote;
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.ReservedRemote:
                if (frame.Type == FrameType.Headers)
                    State = StreamState.HalfClosedLocal;
                else if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.HalfClosedLocal:
                if (frame.IsEndStream || frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            }

            // Server has cleared up more window space
            // Add more to the available window
            if (frame.Type == FrameType.WindowUpdate) {
                var windowUpdateFrame = (WindowUpdateFrame)frame;
                flowControlStateManager.IncreaseWindowSize (StreamIdentifer, windowUpdateFrame.WindowSizeIncrement);
            }

            // Raise the event
            if (OnFrameReceived != null)
            {
                OnFrameReceived.Invoke(frame);
            }
        }

        public void ProcessSentFrame (IFrame frame)
        {
            switch (State) {
            case StreamState.Idle:
                if (frame.Type == FrameType.PushPromise)
                    State = StreamState.ReservedLocal;
                else if (frame.Type == FrameType.Headers)
                    State = StreamState.Open;
                break;
            case StreamState.ReservedLocal:
                if (frame.Type == FrameType.Headers)
                    State = StreamState.HalfClosedRemote;
                else if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.HalfClosedRemote:
                if (frame.IsEndStream || frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.Open:
                if (frame.IsEndStream)
                    State = StreamState.HalfClosedLocal;
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.ReservedRemote:
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            case StreamState.HalfClosedLocal:
                if (frame.Type == FrameType.RstStream)
                    State = StreamState.Closed;
                break;
            }

            // If data frame, decrease available window
            if (frame.Type == FrameType.Data) {
                var windowDecrement = ((DataFrame)frame).PayloadLength;
                flowControlStateManager.DecreaseWindowSize (frame.StreamIdentifier, windowDecrement);
            }

            // Raise the event
            if (OnFrameSent != null)
            {
                OnFrameSent.Invoke(frame);
            }
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
