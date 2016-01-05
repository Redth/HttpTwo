using System;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.Threading;
using System.Collections.Specialized;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using System.Collections.Generic;

namespace HttpTwo
{
    public class Http2Client
    {
        Http2Connection connection;

        public Http2Client (string host, uint port, bool useTls = false)
        {            
            Init (host, port, useTls);
        }

        void Init (string host, uint port, bool useTls = false)
        {
            connection = new Http2Connection (host, port, useTls);
        }

        public X509CertificateCollection Certificates {
            get { return connection.Certificates; }
            set { connection.Certificates = value; }
        }

        public async Task<Http2Response> Post (Uri uri, NameValueCollection headers = null, byte[] data = null)
        {
            return await Send (uri, HttpMethod.Post, headers, data);
        }

        public async Task<Http2Response> Send (Uri uri, HttpMethod method, NameValueCollection headers = null, byte[] data = null)
        {
            var semaphoreClose = new SemaphoreSlim(0);

            await connection.Connect ();

            var stream = await connection.CreateStream ();
            stream.OnFrameReceived += (frame) =>
            {
                if (stream.State == StreamState.Closed)
                    semaphoreClose.Release();
            };

            //await connection.SendFrame(new SettingsFrame());

            var headersFrame = new HeadersFrame (stream.StreamIdentifer);
            headersFrame.Headers.Add (":method", method.Method.ToUpperInvariant ());
            headersFrame.Headers.Add (":path", uri.PathAndQuery);
            headersFrame.Headers.Add (":scheme", uri.Scheme);
            headersFrame.Headers.Add (":authority", uri.Authority);
            if (headers != null && headers.Count > 0)
                headersFrame.Headers.Add (headers);            
            headersFrame.EndHeaders = true;

            if (data != null && data.Length > 0) {

                await connection.SendFrame (headersFrame);

                var dataFrame = new DataFrame (stream.StreamIdentifer);
                dataFrame.Data = data;
                dataFrame.EndStream = true;
                

                await connection.SendFrame (dataFrame);

            } else {                
                headersFrame.EndStream = true;

                await connection.SendFrame (headersFrame);
            }

            if (!await semaphoreClose.WaitAsync (connection.ConnectionTimeout))
                throw new TimeoutException ();
            
            var responseData = new List<byte> ();
            var responseHeaders = new NameValueCollection ();

            foreach (var f in stream.Frames) {
                if (f.Type == FrameType.Headers) {                    
                    responseHeaders = (f as HeadersFrame)?.Headers ?? new NameValueCollection ();
                } else if (f.Type == FrameType.Data) {
                    responseData.AddRange ((f as DataFrame).Data);
                } else if (f.Type == FrameType.Continuation) {
                    var h = (f as ContinuationFrame).Headers ?? new NameValueCollection ();
                    responseHeaders.Add (h);
                } else if (f.Type == FrameType.GoAway) {
                    var fga = f as GoAwayFrame;
                    if (fga != null && fga.AdditionalDebugData != null && fga.AdditionalDebugData.Length > 0)
                        responseData.AddRange (fga.AdditionalDebugData);
                }
            }

            var strStatus = "500";
            if (responseHeaders [":status"] != null)
                strStatus = responseHeaders [":status"];
            
            var statusCode = HttpStatusCode.OK;
            Enum.TryParse<HttpStatusCode> (strStatus, out statusCode);
                

            return new Http2Response {
                Status = statusCode,
                Stream = stream,
                Headers = responseHeaders,
                Body = responseData.ToArray ()
            };
        }

        public class Http2Response
        {
            public HttpStatusCode Status { get; set; }
            public HttpStream Stream { get; set; }
            public NameValueCollection Headers { get;set; }
            public byte[] Body { get;set; }
        }
    }
}

