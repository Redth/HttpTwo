using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpTwo
{
    public class Http2MessageHandler : HttpMessageHandler
    {
        public Http2MessageHandler () : base ()
        {
            connections = new Dictionary<string, Http2Client> ();
        }

        readonly Dictionary<string, Http2Client> connections;

        protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.RequestUri.Scheme + "-" + request.RequestUri.Host + ":" + request.RequestUri.Port;

            if (!connections.ContainsKey (key)) 
                connections.Add (key, new Http2Client (
                    new Http2ConnectionSettings (request.RequestUri.Host, (uint)request.RequestUri.Port, request.RequestUri.Scheme == Uri.UriSchemeHttps)));

            var client = connections [key];

            byte[] data = null;

            if (request.Content != null)
                data = await request.Content.ReadAsByteArrayAsync ().ConfigureAwait (false);

            // Add the other headers (some might not make sense)
            var headers = new NameValueCollection ();
            foreach (var header in request.Headers.AsEnumerable ()) {
                foreach (var value in header.Value)
                    headers.Add (header.Key, value);
            }

            var response = await client.Send (request.RequestUri, request.Method, headers, data).ConfigureAwait (false);

            var httpResponseMsg = new HttpResponseMessage (response.Status);

            foreach (var h in response.Headers.AllKeys) {
                if (!h.StartsWith (":", StringComparison.InvariantCultureIgnoreCase))
                    httpResponseMsg.Headers.TryAddWithoutValidation (h, response.Headers [h]);
            }

            if (response.Body != null)
                httpResponseMsg.Content = new ByteArrayContent (response.Body);

            return httpResponseMsg;
        }
    }
}
