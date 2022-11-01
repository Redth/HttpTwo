using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using hpack;
using HttpTwo.Internal;

namespace HttpTwo
{
    public class Http2ConnectionSettings
    {
        public Http2ConnectionSettings (string url,  X509CertificateCollection certificates = null, Func<TcpClient, Task<Stream>> tlsStreamCreator = null)
            : this (new Uri (url), certificates, tlsStreamCreator)
        {
        }

        public Http2ConnectionSettings (Uri uri,  X509CertificateCollection certificates = null, Func<TcpClient, Task<Stream>> tlsStreamCreator = null)
            : this (uri.Host, (uint)uri.Port, uri.Scheme == Uri.UriSchemeHttps, certificates, tlsStreamCreator)
        {
        }

        public Http2ConnectionSettings (string host, uint port = 80, bool useTls = false, X509CertificateCollection certificates = null, Func<TcpClient, Task<Stream>> tlsStreamCreator = null)
        {
            Host = host;
            Port = port;
            UseTls = useTls;
            Certificates = certificates;
            ConnectionTimeout = TimeSpan.FromSeconds (60);
            DisablePushPromise = false;

            if (useTls)
            {
                if (tlsStreamCreator != null)
                {
                    m_tlsStreamCreator = tlsStreamCreator;
                }
                else
                {
                    m_tlsStreamCreator = async tcp =>
                    {
                        var sslStream = new SslStream(tcp.GetStream(), false,
                                    (sender, certificate, chain, sslPolicyErrors) => true);

                        await sslStream.AuthenticateAsClientAsync(
                            Host,
                            Certificates ?? new X509CertificateCollection(),
                            System.Security.Authentication.SslProtocols.Tls12,
                            false).ConfigureAwait(false);
                        return sslStream;
                    };
                }
            }

        }

        public string Host { get; private set; }
        public uint Port { get; private set; }
        public bool UseTls { get; private set; }
        public X509CertificateCollection Certificates { get; private set; }

        public TimeSpan ConnectionTimeout { get; set; }
        public bool DisablePushPromise { get; set; }

        private Func<TcpClient, Task<Stream>> m_tlsStreamCreator;

        public async Task<Stream> CreateStream(TcpClient tcpClient)
        {
            Stream result = null;
            if (UseTls)
            {
                result = await m_tlsStreamCreator(tcpClient);
            }
            return result;
        }
    }

    public class Http2Connection
    {
        public const string ConnectionPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n";

        static Http2Connection ()
        {
            ServicePointManager.ServerCertificateValidationCallback += 
                (sender, certificate, chain, sslPolicyErrors) => true;
        }

        public Http2Connection (Http2ConnectionSettings connectionSettings, IStreamManager streamManager, IFlowControlManager flowControlManager)
        {
            this.flowControlManager = flowControlManager;
            this.streamManager = streamManager;

            ConnectionSettings = connectionSettings;
            Settings = new Http2Settings ();

            queue = new FrameQueue (flowControlManager);
        }

        public Http2Settings Settings { get; private set; }
        public Http2ConnectionSettings ConnectionSettings { get; private set; }

        IFlowControlManager flowControlManager;
        readonly IStreamManager streamManager;
        readonly FrameQueue queue;

        TcpClient tcp;
        Stream clientStream;

        long receivedDataCount = 0;
        public uint ReceivedDataCount {
            get { return (uint)Interlocked.Read (ref receivedDataCount); }
        }

        public async Task Connect ()
        {
            if (IsConnected ())
                return;

            tcp = new TcpClient ();

            // Disable Nagle for HTTP/2
            tcp.NoDelay = true;

            await tcp.ConnectAsync (ConnectionSettings.Host, (int)ConnectionSettings.Port).ConfigureAwait (false);

            try
            {
                if (ConnectionSettings.UseTls)
                {
                    clientStream = await ConnectionSettings.CreateStream(tcp);
                }
                else
                {
                    clientStream = tcp.GetStream();
                }
            }
            catch
            {
                if (!IsConnected())
                {
                    tcp.Close();
                }
                
                throw;
            }

            // Ensure we have a size for the stream '0'
            flowControlManager.GetWindowSize (0);

            // Send out preface data
            var prefaceData = System.Text.Encoding.ASCII.GetBytes (ConnectionPreface);
            await clientStream.WriteAsync (prefaceData, 0, prefaceData.Length).ConfigureAwait (false);
            await clientStream.FlushAsync ().ConfigureAwait (false);
            
            // Start reading the stream on another thread
            var readTask = Task.Factory.StartNew (() => {
                try { read (); }
                catch (Exception ex) {
                    Log.Debug ("Read error: " + ex);
                    Disconnect ();
                }
            }, TaskCreationOptions.LongRunning);

            readTask.ContinueWith (t => {
                // TODO: Handle the error
                Disconnect ();
            }, TaskContinuationOptions.OnlyOnFaulted).Forget ();

            // Start a thread to handle writing queued frames to the stream
            var writeTask = Task.Factory.StartNew<Task>(write, TaskCreationOptions.LongRunning);
            writeTask.ContinueWith (t => {
                // TODO: Handle the error
                Disconnect ();
            }, TaskContinuationOptions.OnlyOnFaulted).Forget ();

            // Send initial blank settings frame
            var s = new SettingsFrame ();
            if (ConnectionSettings.DisablePushPromise)
                s.EnablePush = false;

            await QueueFrame (s).ConfigureAwait (false);
        }

        public void Disconnect ()
        {
            // complete the blocking collection 
            queue.Complete ();

            // We want to clean up the connection here so let's just try to close/dispose
            // everything

            // Analysis disable EmptyGeneralCatchClause
            try { clientStream.Close (); } catch { }
            try { clientStream.Dispose (); } catch { }

            try { tcp.Client.Shutdown (SocketShutdown.Both); } catch { }
            try { tcp.Client.Dispose (); } catch { }

            try { tcp.Close (); } catch { }
            // Analysis restore EmptyGeneralCatchClause

            tcp = null;
            clientStream = null;
        }

        public bool IsConnected ()
        {
            if (tcp == null || clientStream == null || tcp.Client == null)
                return false;

            if (!tcp.Connected || !tcp.Client.Connected)
                return false;

            if (!tcp.Client.Poll (1000, SelectMode.SelectRead) && !tcp.Client.Poll (1000, SelectMode.SelectWrite))
                return false;

            return true;
        }

        readonly SemaphoreSlim lockWrite = new SemaphoreSlim (1);

        private Decoder m_decoder;
        public Decoder Decoder
        {
            get
            {
                if (m_decoder == null)
                {
                    m_decoder = new Decoder(Settings.MaxHeaderListSize.HasValue ? (int)Settings.MaxHeaderListSize.Value : 8192, (int)Settings.HeaderTableSize);
                }
                return m_decoder;
            }
        }

        private Encoder m_encoder;

        public Encoder Encoder
        {
            get
            {
                if (m_encoder == null)
                {
                    m_encoder = new Encoder((int)Settings.HeaderTableSize);
                }
                return m_encoder;
            }
        }

        public async Task QueueFrame (IFrame frame)
        {
            await queue.Enqueue (frame).ConfigureAwait (false);
        }

        public async Task FreeUpWindowSpace ()
        {
            var sizeToFree = Interlocked.Exchange (ref receivedDataCount, 0);

            if (sizeToFree <= 0)
                return;
            
            await QueueFrame (new WindowUpdateFrame {
                StreamIdentifier = 0,
                WindowSizeIncrement = (uint)sizeToFree
            }).ConfigureAwait (false);
        }

        readonly List<byte> buffer = new List<byte> ();

        async void read ()
        {
            int rx;
            byte[] b = new byte[4096];

            while (true) {

                try {
                    rx = await clientStream.ReadAsync(b, 0, b.Length).ConfigureAwait (false);
                } catch {
                    rx = -1;
                }

                if (rx > 0) {
                    // Add all the bytes read into our buffer list
                    for (int i = 0; i < rx; i++)
                        buffer.Add (b [i]);

                    while (true) 
                    {
                        // We need at least 9 bytes to process the frame 
                        // 9 octets is the frame header length
                        if (buffer.Count < 9)
                            break;

                        // Find out the frame length
                        // which is a 24 bit uint, so we need to convert this as c# uint is 32 bit
                        var flen = new byte[4];
                        flen [0] = 0x0;
                        flen [1] = buffer.ElementAt (0);
                        flen [2] = buffer.ElementAt (1);
                        flen [3] = buffer.ElementAt (2);

                        var frameLength = BitConverter.ToUInt32 (flen.EnsureBigEndian (), 0);

                        // If we are expecting a payload that's bigger than what's in our buffer
                        // we should keep reading from the stream 
                        if (buffer.Count - 9 < frameLength)
                            break;

                        // If we made it this far, the buffer has all the data we need, let's get it out to process
                        var data = buffer.GetRange (0, (int)frameLength + 9).ToArray ();
                        // remove the processed info from the buffer
                        buffer.RemoveRange (0, (int)frameLength + 9);

                        // Get the Frame Type so we can instantiate the right subclass
                        var frameType = data [3]; // 4th byte in frame header is TYPE

                        // we need to turn the stream id into a uint
                        var frameStreamIdData = new byte[4];
                        Array.Copy (data, 5, frameStreamIdData, 0, 4);
                        uint frameStreamId = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

                        // Create a new typed instance of our abstract Frame
                        var frame = Frame.Create ((FrameType)frameType);

                        try {
                            // Call the specific subclass implementation to parse
                            frame.Parse (data);
                        } catch (Exception ex) {
                            Log.Error ("Parsing Frame Failed: {0}", ex);
                            throw;
                        }

                        Log.Debug ("<- {0}", frame);

                        // If it's a settings frame, we should note the values and
                        // return the frame with the Ack flag set
                        if (frame.Type == FrameType.Settings) {

                            var settingsFrame = frame as SettingsFrame;

                            // Update our instance of settings with the new data
                            Settings.UpdateFromFrame (settingsFrame, flowControlManager);

                            // See if this was an ack, if not, return an empty 
                            // ack'd settings frame
                            if (!settingsFrame.Ack)
                                await QueueFrame (new SettingsFrame { Ack = true }).ConfigureAwait (false);

                        } else if (frame.Type == FrameType.Ping) {

                            var pingFrame = frame as PingFrame;
                            // See if we need to respond to the ping request (if it's not-ack'd)
                            if (!pingFrame.Ack) {
                                // Ack and respond
                                pingFrame.Ack = true;
                                await QueueFrame (pingFrame).ConfigureAwait (false);
                            }

                        } else if (frame.Type == FrameType.Data) {

                            // Increment our received data counter
                            Interlocked.Add (ref receivedDataCount, frame.PayloadLength);
                        }
                        else if (frame.Type == FrameType.GoAway)
                        {
                            Disconnect();
                            return;
                        }

                        // Some other frame type, just pass it along to the stream
                        var stream = await streamManager.Get(frameStreamId).ConfigureAwait (false);
                        stream.ProcessReceivedFrames(frame);
                    }

                } else {
                    // Stream was closed, break out of reading loop
                    break;
                }
            }

            // Cleanup
            Disconnect();
        }

        async Task write ()
        {
            foreach (var frame in queue.GetConsumingEnumerable ()) {
                if (frame == null) {
                    Log.Info ("Null frame dequeued");
                    continue;
                }

                Log.Debug ("-> {0}", frame);

                var data = frame.ToBytes ().ToArray ();

                await lockWrite.WaitAsync ().ConfigureAwait (false);

                try {
                    await clientStream.WriteAsync(data, 0, data.Length).ConfigureAwait (false);
                    await clientStream.FlushAsync().ConfigureAwait (false);
                    var stream = await streamManager.Get (frame.StreamIdentifier).ConfigureAwait (false);
                    stream.ProcessSentFrame (frame);
                } catch (Exception ex) {
                    Log.Warn ("Error writing frame: {0}, {1}", frame.StreamIdentifier, ex);
                } finally {
                    lockWrite.Release();
                }
            }
        }
    }
}
