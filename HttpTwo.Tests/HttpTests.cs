using Xunit;
using System;
using System.Net.Http;
using System.Collections.Specialized;
using System.Threading;
using HttpTwo.Internal;

namespace HttpTwo.Tests
{
	public class HttpTests : IDisposable
    {
        const bool UseInternalHttpRunner = true;

        NodeHttp2Runner node;

        public HttpTests ()
        {
            // Setup logger 
            Log.Logger = new ConsoleLogger { Level = LogLevel.Info };

            if (UseInternalHttpRunner) {
                node = new NodeHttp2Runner ();
                //node.LogHandler = Console.WriteLine;
            
                node.StartServer ();
                // Wait for the server to initialize
                Thread.Sleep (2000);
            }
        }

        public void Dispose ()
        {     
            if (UseInternalHttpRunner)
                node.StopServer ();
        }

        [Fact]
        public async void Get_Single_Html_Page ()
        {
            var http2MsgHandler = new Http2MessageHandler ();
            var http = new HttpClient (http2MsgHandler);

            var data = await http.GetStringAsync ("http://localhost:8999/index.html");

            Assert.NotEmpty (data);
            Assert.True (data.Contains ("Hello World"));
        }

        //[Test]
        public async void Get_Single_Html_Page_Https ()
        {
            var http2MsgHandler = new Http2MessageHandler ();
            var http = new HttpClient (http2MsgHandler);

            var data = await http.GetStringAsync ("https://localhost:8999/index.html");

            Assert.NotEmpty (data);
            Assert.True (data.Contains ("Hello World"));
        }

        [Fact]
        public async void Get_Multiple_Html_Pages ()
        {
            var http2MsgHandler = new Http2MessageHandler ();
            var http = new HttpClient (http2MsgHandler);

            for (int i = 0; i < 3; i++) {
                var data = await http.GetStringAsync ("http://localhost:8999/index.html");

                Assert.NotEmpty (data);
                Assert.True (data.Contains ("Hello World"));
            }
        }


        [Fact]
        public async void Settings_Disable_Push_Promise ()
        {
            var url = new Uri ("http://localhost:8999/index.html");
            var settings = new Http2ConnectionSettings (url) { DisablePushPromise = true };
            var http = new Http2Client (settings);

            await http.Connect ();

            var didAck = false;
            var semaphoreSettings = new SemaphoreSlim (0);
            var cancelTokenSource = new CancellationTokenSource ();

            var connectionStream = await http.StreamManager.Get (0);
            connectionStream.OnFrameReceived += (frame) => {
                // Watch for an ack'd settings frame after we sent the frame with no push promise
                if (frame.Type == FrameType.Settings) {
                    if ((frame as SettingsFrame).Ack) {
                        didAck = true;
                        semaphoreSettings.Release ();
                    }
                }
            };

            cancelTokenSource.CancelAfter (TimeSpan.FromSeconds (2));

            await semaphoreSettings.WaitAsync (cancelTokenSource.Token);

            Assert.True (didAck);
        }


        [Fact]
        public async void Get_Send_Headers_With_Continuation ()
        {
            var uri = new Uri ("http://localhost:8999/index.html");
            var http = new Http2Client (uri);

            // Generate some gibberish custom headers
            var headers = new NameValueCollection ();
            for (int i = 0; i < 1000; i++)
                headers.Add ("custom-" + i, "HEADER-VALUE-" + i);

            var response = await http.Send (uri, HttpMethod.Get, headers, new byte[0]);

            var data = System.Text.Encoding.ASCII.GetString (response.Body);

            Assert.NotEmpty (data);
            Assert.True (data.Contains ("Hello World"));
        }

        [Fact]
        public async void Ping ()
        {
            var uri = new Uri ("http://localhost:8999/index.html");
            var http = new Http2Client (uri);

            var data = System.Text.Encoding.ASCII.GetBytes ("PINGPONG");

            var cancelTokenSource = new CancellationTokenSource ();
            cancelTokenSource.CancelAfter (TimeSpan.FromSeconds (2));

            var pong = await http.Ping (data, cancelTokenSource.Token);

            Assert.True (pong);
        }

        [Fact]
        public async void GoAway ()
        {
            var uri = new Uri ("http://localhost:8999/index.html");
            var http = new Http2Client (uri);

            await http.Connect ();

            var cancelTokenSource = new CancellationTokenSource ();
            cancelTokenSource.CancelAfter (TimeSpan.FromSeconds (2));

            var sentGoAway = await http.Disconnect ();

            Assert.True (sentGoAway);
        }
    }
}

