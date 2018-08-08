using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HttpTwo.Internal
{
    public interface IStreamManager
    {
        uint GetNextIdentifier ();
        Task<Http2Stream> Get (uint streamIdentifier);
        Task<Http2Stream> Get ();
        Task Cleanup (uint streamIdentifier);
    }

    public class StreamManager : IStreamManager
    {
        const uint STREAM_ID_MAX_VALUE = 1073741823;

        uint nextStreamId = 1;

        public uint GetNextIdentifier ()
        {
            var nextId = nextStreamId;

            // Increment for next use, by 2, must always be odd if initiated from client
            nextStreamId += 2;

            // Wrap around if we hit max
            if (nextStreamId > STREAM_ID_MAX_VALUE) {
                // TODO: Disconnect so we can reset the stream id
            }

            return nextId;
        }

        public StreamManager (IFlowControlManager flowControlManager)
        {
            this.flowControlManager = flowControlManager;

            streams = new Dictionary<uint, Http2Stream>
            {
                // Add special stream '0' to act as connection level
                { 0, new Http2Stream(this.flowControlManager, 0) }
            };
        }

        readonly IFlowControlManager flowControlManager;

        Dictionary<uint, Http2Stream> streams;

        readonly SemaphoreSlim lockStreams = new SemaphoreSlim (1);

        public async Task<Http2Stream> Get (uint streamIdentifier)
        {
            await lockStreams.WaitAsync ().ConfigureAwait (false);

            Http2Stream stream = null;

            if (!streams.ContainsKey (streamIdentifier)) {
                stream = new Http2Stream (flowControlManager, streamIdentifier);
                streams.Add (streamIdentifier, stream);
            } else {
                stream = streams [streamIdentifier];
            }

            lockStreams.Release ();

            return stream;
        }

        public async Task<Http2Stream> Get ()
        {
            await lockStreams.WaitAsync ().ConfigureAwait (false);

            var stream = new Http2Stream (flowControlManager, GetNextIdentifier ());

            streams.Add (stream.StreamIdentifer, stream);

            lockStreams.Release ();

            return stream;
        }

        public async Task Cleanup (uint streamIdentifier)
        {
            await lockStreams.WaitAsync ().ConfigureAwait (false);

            if (streams.ContainsKey (streamIdentifier))
                streams.Remove (streamIdentifier);

            lockStreams.Release ();
        }
    }
}
