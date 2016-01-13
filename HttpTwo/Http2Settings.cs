using HttpTwo.Internal;

namespace HttpTwo
{
    public class Http2Settings
    {
        public const uint DefaultWindowSize = 65535;
        public const uint DefaultMaxFrameSize = 16384;
        public const uint DefaultHeaderTableSize = 4096;

        // 4096 is default (0x1 index)
        public uint HeaderTableSize { get; set; } = DefaultHeaderTableSize;

        // 1 is default (true) (0x2 index)
        public bool EnablePush { get;set; } = true;

        // no limit initially (0x3 index)
        public uint? MaxConcurrentStreams { get;set; }

        // 65,535 is default (0x4 index)
        public uint InitialWindowSize { get;set; } = DefaultWindowSize;

        // 16,384 is default (0x5 index)
        public uint MaxFrameSize { get;set; } = DefaultMaxFrameSize;

        // no limit initially (0x6 index)
        public uint? MaxHeaderListSize { get;set; }

        public void UpdateFromFrame (SettingsFrame frame, IFlowControlManager flowControlStateManager)
        {
            if (frame.EnablePush.HasValue)
                EnablePush = frame.EnablePush.Value;
            if (frame.HeaderTableSize.HasValue)
                HeaderTableSize = frame.HeaderTableSize.Value;
            if (frame.MaxConcurrentStreams.HasValue)
                MaxConcurrentStreams = frame.MaxConcurrentStreams.Value;
            if (frame.MaxFrameSize.HasValue)
                MaxFrameSize = frame.MaxFrameSize.Value;
            if (frame.MaxHeaderListSize.HasValue)
                MaxHeaderListSize = frame.MaxHeaderListSize.Value;

            if (frame.InitialWindowSize.HasValue) {
                InitialWindowSize = frame.InitialWindowSize.Value;

                // Try and update the flow control manager's initial size
                if (flowControlStateManager != null)
                    flowControlStateManager.InitialWindowSize = InitialWindowSize;
            }
        }
    }
}
