using System;

namespace HttpTwo
{
    public class Http2Settings
    {
        public Http2Settings ()
        {            
        }

        // 4096 is default (0x1 index)
        public uint HeaderTableSize { get; set; } = 4096;

        // 1 is default (true) (0x2 index)
        public bool EnablePush { get;set; } = true;

        // no limit initially (0x3 index)
        public uint? MaxConcurrentStreams { get;set; }

        // 16,384 is default (0x4 index)
        public uint InitialWindowSize { get;set; } = 16384;

        // 65,535 is default (0x5 index)
        public uint MaxFrameSize { get;set; } = 65535;

        // no limit initially (0x6 index)
        public uint? MaxHeaderListSize { get;set; }
    }
}

