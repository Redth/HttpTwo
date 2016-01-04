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

        static uint GetNextId ()
        {
            var nextId = streamId;

            // Increment for next use, by 2, must always be odd if initiated from client
            streamId += 2;

            // Wrap around if we hit max
            if (streamId > STREAM_ID_MAX_VALUE)
                streamId = 1;

            return nextId;
        }

        TaskCompletionSource<object> tcsEndStream;

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
            StreamIdentifer = streamIdentifier;
            State = StreamState.Idle;

            tcsEndStream = new TaskCompletionSource<object> ();
        }

        public uint StreamIdentifer { get; private set; }

        public StreamState State { get; set; }

        public List<Frame> Frames { get;set; }


        public void ProcessFrame (Frame frame)
        {   
            // Add frame to the list of history
            Frames.Add (frame);

            if (frame.Type == FrameType.RstStream || frame.IsEndStream) {
                State = StreamState.Closed;
            } else {
                
                if (State == StreamState.Idle) {
                    if (frame.Type == FrameType.Headers) {
                        State = StreamState.Open;
                    }
                }
                if (frame.Type == FrameType.PushPromise && State == StreamState.Idle) {
                    State = StreamState.ReservedRemote;
                }
            }

            // Raise the event
            OnFrameReceived?.Invoke (frame);

            if (State == StreamState.Closed && !tcsEndStream.Task.IsCompleted)
                tcsEndStream.SetResult (null);
        }

        public async Task<bool> WaitForEndStreamAsync (TimeSpan timeout)
        {                        
            if (await Task.WhenAny (tcsEndStream.Task, Task.Delay (timeout)) == tcsEndStream.Task) {
                // task completed within timeout
                return true;
            } 

            return false;
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
