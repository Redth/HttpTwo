using System;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.Threading;
using System.Collections.Specialized;
using System.Text;

namespace HttpTwo
{
    public class Http2Client
    {
        Http2Connection connection;

        public Http2Client (string host, uint port, bool useTls = false)
        {
            var ipHost = Dns.GetHostEntry (host);
            var endPoint = new IPEndPoint (ipHost.AddressList.FirstOrDefault (), (int)port);

            Init (endPoint, useTls);
        }

        public Http2Client (IPEndPoint endPoint, bool useTls = false)
        {
            Init (endPoint, useTls);
        }

        void Init (IPEndPoint endPoint, bool useTls = false)
        {
            connection = new Http2Connection (endPoint, useTls);
        }

        public async Task Get (Uri uri)
        {
            await connection.Connect ();

            var resetComplete = new ManualResetEventSlim (false);

            var stream = await connection.CreateStream ();
            stream.OnFrameReceived += frame => {
                if (frame.IsEndStream)
                    resetComplete.Set ();
            };

            var headersFrame = new HeadersFrame (stream.StreamIdentifer);
            headersFrame.Headers.Add (":method", "GET");
            headersFrame.Headers.Add (":path", uri.PathAndQuery);
            headersFrame.Headers.Add (":scheme", uri.Scheme);
            headersFrame.Headers.Add (":authority", uri.Authority);
            headersFrame.EndHeaders = true;
            headersFrame.EndStream = true;

            await connection.SendFrame (headersFrame);

            resetComplete.Wait (connection.ConnectionTimeout);

            var responseData = string.Empty;
            var responseHeaders = new NameValueCollection ();

            foreach (var f in stream.Frames) {
                if (f.Type == FrameType.Headers) {                    
                    responseHeaders = (f as HeadersFrame)?.Headers ?? new NameValueCollection ();
                } else if (f.Type == FrameType.Data) {
                    responseData += Encoding.ASCII.GetString ((f as DataFrame).Data);
                } else if (f.Type == FrameType.Continuation) {
                    // TODO:
                }
            }


        }
    }
}

