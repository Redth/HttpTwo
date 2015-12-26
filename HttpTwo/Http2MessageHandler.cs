using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Text;
using System.Linq;
using System.Net;
using System.Collections.Generic;

namespace HttpTwo
{
    public class Http2MessageHandler : HttpMessageHandler
    {
        public Http2MessageHandler () : base ()
        {
        }

        Http2Connection connection;

        #region implemented abstract members of HttpMessageHandler

        protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            if (connection == null) {
                connection = new Http2Connection (request.RequestUri.Host, (uint)request.RequestUri.Port, request.RequestUri.Scheme == Uri.UriSchemeHttps);
            } else {
                // TODO: Check if they tried to use a new host/port
            }

            await connection.Connect ();

            var resetComplete = new ManualResetEventSlim (false);

            var stream = await connection.CreateStream ();
            stream.OnFrameReceived += frame => {
                if (frame.IsEndStream)
                    resetComplete.Set ();
            };

            var headersFrame = new HeadersFrame (stream.StreamIdentifer);
            headersFrame.Headers.Add (":method", request.Method.Method.ToUpperInvariant ());
            headersFrame.Headers.Add (":path", request.RequestUri.PathAndQuery);
            headersFrame.Headers.Add (":scheme", request.RequestUri.Scheme);
            headersFrame.Headers.Add (":authority", request.RequestUri.Authority);
            headersFrame.EndHeaders = true;
            headersFrame.EndStream = true;

            await connection.SendFrame (headersFrame);

            resetComplete.Wait (connection.ConnectionTimeout);

            var responseData = new List<byte> ();
            var responseHeaders = new NameValueCollection ();

            foreach (var f in stream.Frames) {
                if (f.Type == FrameType.Headers) {                    
                    responseHeaders = (f as HeadersFrame)?.Headers ?? new NameValueCollection ();
                } else if (f.Type == FrameType.Data) {
                    responseData.AddRange ((f as DataFrame).Data);
                    //responseData += Encoding.ASCII.GetString ((f as DataFrame).Data);
                } else if (f.Type == FrameType.Continuation) {
                    // TODO:
                }
            }

            var httpStatusStr = responseHeaders.GetValues (":status")?.FirstOrDefault ();
            var httpStatus = HttpStatusCode.InternalServerError;

            Enum.TryParse<HttpStatusCode> (httpStatusStr, out httpStatus);

            var httpResponseMsg = new HttpResponseMessage (httpStatus);

            foreach (var h in responseHeaders.AllKeys) {
                if (!h.StartsWith (":"))
                    httpResponseMsg.Headers.TryAddWithoutValidation (h, responseHeaders [h]);
            }

            httpResponseMsg.Content = new ByteArrayContent (responseData.ToArray ());

            return httpResponseMsg;
        }

        #endregion
    }
}

