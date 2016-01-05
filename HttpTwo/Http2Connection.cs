using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace HttpTwo
{
    public class Http2Connection
    {
        public const string ConnectionPreface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n";

        static Http2Connection ()
        {
            ServicePointManager.ServerCertificateValidationCallback += 
                (sender, certificate, chain, sslPolicyErrors) => true;
        }

        public Http2Connection (string host, uint port, bool useTls = false)
        {
            Init(host, port, useTls);
        }
        
        void Init (string host, uint port, bool useTls = false)
        {
            Host = host;
            Port = port;
            UseTls = useTls;
            ConnectionTimeout = TimeSpan.FromSeconds (60);
            Streams = new Dictionary<uint, HttpStream> ();
        }

        public X509CertificateCollection Certificates { get; set; }
        public bool UseTls { get; private set; }
        public string Host { get; private set; }
        public uint Port { get; private set; }
        public TimeSpan ConnectionTimeout { get; set; }

        public Dictionary<uint, HttpStream> Streams { get; private set; }

        TcpClient tcp;
        Stream clientStream;
        SslStream sslStream;

        ManualResetEventSlim resetEventConnectionSettingsFrame;

        public async Task Connect ()
        {
            if (IsConnected ())
                return;

            tcp = new TcpClient ();

            // Disable Nagle for HTTP/2
            tcp.NoDelay = true;

            await tcp.ConnectAsync (Host, (int)Port);

            if (UseTls) {
                sslStream = new SslStream (tcp.GetStream (), false, 
                    (sender, certificate, chain, sslPolicyErrors) => true);
                
                await sslStream.AuthenticateAsClientAsync (
                    Host, 
                    Certificates ?? new X509CertificateCollection (), 
                    System.Security.Authentication.SslProtocols.Tls12, 
                    false);

                clientStream = sslStream;
            } else {
                clientStream = tcp.GetStream ();
            }

            // Send out preface data
            var prefaceData = System.Text.Encoding.ASCII.GetBytes (ConnectionPreface);
            await clientStream.WriteAsync (prefaceData, 0, prefaceData.Length);
            await clientStream.FlushAsync ();
            
            // Start reading the stream on another thread
            var readTask = Task.Factory.StartNew (() => {
                try { read (); }
                catch (Exception ex) {
                    Console.WriteLine ("Read error: " + ex);
                    Disconnect ();
                }
            }, TaskCreationOptions.LongRunning);

            readTask.ContinueWith (t => {
                // TODO: Handle the error
                Disconnect ();
            }, TaskContinuationOptions.OnlyOnFaulted);

            // Send an un-ACK'd settings frame
            await SendFrame(new SettingsFrame()); // { EnablePush = false });
        }

        void Disconnect ()
        {
            //We now expect apple to close the connection on us anyway, so let's try and close things
            // up here as well to get a head start
            //Hopefully this way we have less messages written to the stream that we have to requeue
            try { clientStream.Close (); } catch { }
            try { clientStream.Dispose (); } catch { }

            if (UseTls && sslStream != null) {
                try { sslStream.Close (); } catch { }
                try { sslStream.Dispose (); } catch { }
            }

            try { tcp.Client.Shutdown (SocketShutdown.Both); } catch { }
            try { tcp.Client.Dispose (); } catch { }

            try { tcp.Close (); } catch { }

            tcp = null;
            sslStream = null;
            clientStream = null;
        }

        bool IsConnected ()
        {
            if (tcp == null || clientStream == null || tcp.Client == null)
                return false;

            if (!tcp.Connected || !tcp.Client.Connected)
                return false;

            if (!tcp.Client.Poll (1000, SelectMode.SelectRead)
                || !tcp.Client.Poll (1000, SelectMode.SelectWrite))
                return false;

            return true;
        }

        SemaphoreSlim lockStreams = new SemaphoreSlim (1);

        public async Task<HttpStream> GetStream (uint streamIdentifier)
        {
            await lockStreams.WaitAsync ();

            HttpStream stream = null;

            if (!Streams.ContainsKey (streamIdentifier)) {
                stream = new HttpStream (streamIdentifier);
                Streams.Add (streamIdentifier, stream);
            } else {
                stream = Streams [streamIdentifier];
            }

            lockStreams.Release ();

            return stream;
        }

        public async Task<HttpStream> CreateStream ()
        {
            await lockStreams.WaitAsync ();

            var stream = new HttpStream ();

            Streams.Add (stream.StreamIdentifer, stream);

            lockStreams.Release ();

            return stream;
        }

        public async Task CleanupStream (uint streamIdentifier)
        {
            await lockStreams.WaitAsync ();

            if (Streams.ContainsKey (streamIdentifier))
                Streams.Remove (streamIdentifier);

            lockStreams.Release ();
        }

        SemaphoreSlim lockWrite = new SemaphoreSlim (1);

        public async Task SendFrame (Frame frame)
        {
            var data = frame.ToBytes ().ToArray ();

            await lockWrite.WaitAsync ();

            try {
                await clientStream.WriteAsync(data, 0, data.Length);
                await clientStream.FlushAsync();
                var stream = await GetStream (frame.StreamIdentifier);
                stream.ProcessSentFrame (frame);
            } catch {
            } finally {
                lockWrite.Release();
            }
        }

        readonly List<byte> buffer = new List<byte> ();

        async void read ()
        {                        
            int rx = 0;
            byte[] b = new byte[4096];

            while (true) {

                try {
                    rx = await clientStream.ReadAsync(b, 0, b.Length);
                } catch {
                    rx = -1;
                }

                if (rx > 0) {

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

                        // Don't need the flags yet
                        //var frameFlags = data [4]; // 5th byte is FLAGS

                        // we need to turn the stream id into a uint
                        var frameStreamIdData = new byte[4]; 
                        Array.Copy (data, 5, frameStreamIdData, 0, 4);
                        uint frameStreamId = Util.ConvertFromUInt31 (frameStreamIdData.EnsureBigEndian ());

                        Frame frame = null;

                        var ft = (FrameType)frameType;

                        switch (ft) {
                        case FrameType.Data:
                            frame = new DataFrame ();
                            break;
                        case FrameType.Headers:
                            frame = new HeadersFrame ();
                            break;
                        case FrameType.Priority:
                            frame = new PriorityFrame ();
                            break;
                        case FrameType.RstStream:
                            frame = new RstStreamFrame ();
                            break;
                        case FrameType.Settings:
                            frame = new SettingsFrame ();
                            break;
                        case FrameType.PushPromise:
                            frame = new PushPromiseFrame ();
                            break;
                        case FrameType.Ping:
                            frame = new PingFrame ();
                            break;
                        case FrameType.GoAway:
                            frame = new GoAwayFrame ();
                            break;
                        case FrameType.WindowUpdate:
                            frame = new WindowUpdateFrame ();
                            break;
                        case FrameType.Continuation:
                            frame = new ContinuationFrame ();
                            break;
                        }

                        try {
                            // Call the specific subclass implementation to parse
                            if (frame != null)
                                frame.Parse (data);
                        } catch (Exception ex) {
                            Console.WriteLine ("Parsing Frame Failed: " + ex);
                            throw ex;
                        }

                        // If we are waiting on the connection preface from server
                        // and it's the right frame type, set our resetevent
                        if (ft == FrameType.Settings 
                            && resetEventConnectionSettingsFrame != null 
                            && !resetEventConnectionSettingsFrame.IsSet) {

                            var settingsFrame = frame as SettingsFrame;
                            // ack the settings from the server
                            settingsFrame.Ack = true;
                            await SendFrame (settingsFrame);
                        }

                        // TODO: Process window update
                        if (ft == FrameType.WindowUpdate) {

                        }

                        try {
                            if (frameStreamId == 0) {
                                foreach (var s in Streams) {
                                    if (s.Value.State != StreamState.Closed)
                                        s.Value.ProcessFrame(frame);
                                }
                            } else {
                                var stream = await GetStream(frameStreamId);
                                stream.ProcessFrame(frame);
                            }
                        } catch (Exception ex) {
                            Console.WriteLine ("Error Processing Frame: " + ex);
                            throw ex;
                        }
                    }

                } else {
                    // Stream was closed, break out of reading loop
                    break;
                }
            }

            // Cleanup
            Disconnect();
        }
    }

}

