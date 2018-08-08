using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace HttpTwo.Internal
{
    public class FrameQueue
    {
        // 1 == highest, 1000 == lowest
        static readonly int[] PRIORITIES = { 1, 10, 100, 1000 };

        Dictionary<int, List<IFrame>> frameQueues = new Dictionary<int, List<IFrame>> ();

        public FrameQueue (IFlowControlManager flowControlManager)
        {
            this.flowControlManager = flowControlManager;

            // Signal the possible presence of data in the queue if the window size increases
            // to allow any paused frames to be sent
            this.flowControlManager.FlowControlWindowSizeIncreased += (streamIdentifier, increasedByAmount) =>
                waitAny.Set ();

            // Add our priority lists
            frameQueues = new Dictionary<int, List<IFrame>> ();
            foreach (var p in PRIORITIES)
                frameQueues.Add (p, new List<IFrame> ());
        }

        IFlowControlManager flowControlManager;

        // Lock for reading/writing from list
        SemaphoreSlim semaphoreFrames = new SemaphoreSlim (1);

        // Reset event to help block Dequeueing until items are available
        ManualResetEventSlim waitAny = new ManualResetEventSlim (false);

        CancellationTokenSource cancelTokenSource = new CancellationTokenSource ();

        public async Task Enqueue (IFrame frame)
        {
            await semaphoreFrames.WaitAsync ().ConfigureAwait (false);

            try {
                var priority = GetPriority (frame.Type);

                frameQueues[priority].Add (frame);

                // Signal that there is an item to process
                waitAny.Set ();

            } finally {
                semaphoreFrames.Release ();
            }
        }

        public IEnumerable<IFrame> GetConsumingEnumerable ()
        {
            IFrame result = null;

            // Loop until we get a result, unless cancelled
            while (!cancelTokenSource.IsCancellationRequested) {

                // Wait for a signal that there's a frame
                waitAny.Wait (cancelTokenSource.Token);

                // attempt to dequeue a frame
                // this could be null if our queue is paused
                result = Dequeue ();

                // If null, queue was paused, so let's keep waiting
                // reset our handle and loop again
                if (result == null)
                    waitAny.Reset ();
                else
                    yield return result;
            }
        }

        public void Complete() => cancelTokenSource.Cancel();

        IFrame Dequeue ()
        {
            IFrame result = null;

            semaphoreFrames.Wait ();

            foreach (var priority in PRIORITIES) {
                var frames = frameQueues [priority];

                if (frames.Count > 0) {

                    var frameIndex = -1; // counter of position in the loop

                    // Loop until we find a frame that we can send (if so)
                    foreach (var frame in frames) {

                        frameIndex++; // we use this later to check for items in the queue prior to the current frame

                        // Consider that we may need to skip data frames
                        // if the flow control window is depleted or too small for the payload
                        if (frame.Type == FrameType.Data) {

                            // See if either the connection flow control or the frame's stream's flow control
                            // window size has capacity for the data payload
                            // if not, let's skip over this frame
                            if (flowControlManager.GetWindowSize (0) - frame.PayloadLength < 0
                                || flowControlManager.GetWindowSize (frame.StreamIdentifier) - frame.PayloadLength < 0)
                                continue;

                        } else if (frame.Type == FrameType.Headers || frame.Type == FrameType.Continuation) {

                            // If we have HEADERS or CONTINUATION frames these are considered trailers and could appear after
                            // a DATA frame in the queue.
                            // Problem is, if the DATA frame needs to be paused for flow control,
                            // we also want to pause the trailing frames, so let's look and see
                            // if the frame has any DATA frames still in the queue before it, from the same stream
                            // if so, skip sending the trailers.
                            if (frames.Take (frameIndex)
                                .Any (f => f.StreamIdentifier == frame.StreamIdentifier && f.Type == FrameType.Data))
                                continue;
                        }

                        // If we made it this far, we found a frame we can send
                        // Let's keep it and stop looking for more in this list
                        result = frame;
                        break;
                    }

                    if (result != null) {

                        // Remove the found item from the list
                        frames.Remove (result);

                        // exit the loop
                        break;
                    }
                }
            }

            semaphoreFrames.Release ();

            return result;
        }


        int GetPriority (FrameType frameType)
        {
            switch (frameType) {
            case FrameType.Ping:
            case FrameType.Settings:
                return 1;
            case FrameType.WindowUpdate:
            case FrameType.RstStream:
            case FrameType.GoAway:
                return 10;
            case FrameType.PushPromise:
            case FrameType.Headers:
                return 100;
            default:
                return 1000;
            }
        }
    }
}
