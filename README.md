# HttpTwo

A basic C# HTTP/2 client library implementation

The focus of this library is to bring enough HTTP/2 functionality to .NET for implementing the APNS (Apple Push Notification Service) provider API over HTTP/2 within [PushSharp](https://github.com/redth/pushsharp)

It's currently very untested and only partially implemented.

**What's working:**
 - All frame types can be parsed and can be generated to send to a stream
 - HPack for frames that send headers
 - Simple requests should work

**What's not working / not implemented:**
 - Secure connections require TLS 1.2 according to the RFC so they won't work on Mono at this point
 - No flow control is in place
 - Stream prioritization isn't implemented (you can still send Priority frames)
 - Push Promise isn't implemented
 - Much more test coverage needed
 - No ALPN support - only starting with prior knowledge which Apple APNS uses for TLS
 - No Upgrade support (only starting with prior knowledge in clear text)
 - HttpClient Support (Http2MessageHandler)
   - Passes along all headers - might not make sense always

### Using the Http2Client

There's a `Http2Client` class that can be used to send requests and receive responses:

```csharp
// Uri to request
var uri = new Uri ("http://somesite.com:80/index.html");

// Create a Http2Client
var http2 = new Http2Client (uri.Host, (uint)uri.Port, useTls: uri.Scheme == Uri.UriSchemeHttps);

// Specify any custom headers
var headers = new NameValueCollection ();
headers.Add ("some-header", "value");

// For some requests you may have a request body
byte[] data = null; 

// Await our response
var response = await http2.Send (uri, HttpMethod.Get, headers, data); 

// Response object has properties:
//  HttpStatusCode Status
//  HttpStream Stream (contains Frames history)
//  NameValueCollection Headers
//  byte[] Body
// ...
```

### Using the HttpClient API

For some familiarity I've implemented a simple `HttpMessageHandler` so that you can use the HttpClient API:

```csharp
// Create our HttpTwo handler
var handler = new Http2MessageHandler ();
// Pass the handler into the HttpClient
var http = new HttpClient (handler);

var data = await http.GetStringAsync ("http://somesite.com:80/index.html");
```

### Running Tests

There's only one simple test right now for a single page get.

Tests will run against a `node-http2` server running locally.  The source is included in `HttpTwo.Tests/node-http2`.  Make sure you run `npm install` in that folder after check out before you start the NUnit tests (which will automatically launch the node server when you run the tests).

### License

Copyright 2015 Jonathan Dick

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.


### HPack Code
This library uses HPACK code which is licensed under Apache 2.0 and was borrowed from Ringo Leese's repository at: https://github.com/ringostarr80/hpack 

Thanks @ringostarr80 !
