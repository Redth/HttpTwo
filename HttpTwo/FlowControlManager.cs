using System;
using System.Collections.Generic;

namespace HttpTwo.Internal
{
    public delegate void FlowControlWindowSizeIncreasedDelegate (uint streamIdentifier, uint increasedByAmount);

    public interface IFlowControlManager
    {
        event FlowControlWindowSizeIncreasedDelegate FlowControlWindowSizeIncreased;

        uint InitialWindowSize { get; set; }
        uint GetWindowSize (uint streamIdentifier);
        void DecreaseWindowSize (uint streamIdentifier, uint decreaseByAmount);
        void IncreaseWindowSize (uint streamIdentifier, uint increaseByAmount);
    }

    public class FlowControlManager : IFlowControlManager
    {
        public FlowControlManager ()
        {
            InitialWindowSize = Http2Settings.DefaultWindowSize;

            windowSizes = new Dictionary<uint, uint> ();
        }

        public event FlowControlWindowSizeIncreasedDelegate FlowControlWindowSizeIncreased;

        readonly Dictionary<uint, uint> windowSizes;

        public uint InitialWindowSize { get; set; }

        public uint GetWindowSize (uint streamIdentifier)
        {
            if (!windowSizes.ContainsKey (streamIdentifier))
                windowSizes.Add (streamIdentifier, Http2Settings.DefaultWindowSize);

            return windowSizes [streamIdentifier];
        }

        public void DecreaseWindowSize (uint streamIdentifier, uint decreaseByAmount)
        {
            var current = GetWindowSize (streamIdentifier);

            var newAmount = current - decreaseByAmount;
            if (newAmount < 0)
                newAmount = 0;

            windowSizes [streamIdentifier] = newAmount;
        }

        public void IncreaseWindowSize (uint streamIdentifier, uint increaseByAmount)
        {
            var current = GetWindowSize (streamIdentifier);

            var newAmount = current + increaseByAmount;

            windowSizes [streamIdentifier] = newAmount;

            // Fire the event
            FlowControlWindowSizeIncreased?.Invoke (streamIdentifier, increaseByAmount);
        }
    }
}
