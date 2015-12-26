using NUnit.Framework;
using System;
using System.Net.Http;

namespace HttpTwo.Tests
{
    [TestFixture]
    public class HttpTests
    {
        NodeHttp2Runner node;

        [SetUp]
        public void Setup ()
        {
            node = new NodeHttp2Runner ();
            node.LogHandler = (msg) => {
                Console.WriteLine (msg);
            };
            node.StartServer ();
            // Wait for the server to initialize
            System.Threading.Thread.Sleep (2000);
        }

        [TearDown]
        public void Teardown ()
        {
            node.StopServer ();
        }

        [Test]
        public async void Get_Single_Html_Page ()
        {
            var http2MsgHandler = new Http2MessageHandler ();
            var http = new HttpClient (http2MsgHandler);

            var data = await http.GetStringAsync ("http://localhost:8999/index.html");

            Assert.IsNotNullOrEmpty (data);
            Assert.IsTrue (data.Contains ("Hello World"));
        }
    }
}

