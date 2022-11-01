# HttpTwo

A fully managed C# HTTP/2 client library implementation

The focus of this library is to bring enough HTTP/2 functionality to .NET for implementing the APNS (Apple Push Notification Service) provider API over HTTP/2 within [PushSharp](https://github.com/redth/pushsharp)

[![Join the chat at https://gitter.im/Redth/HttpTwo](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/Redth/HttpTwo?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

[![AppVeyor CI Status](https://ci.appveyor.com/api/projects/status/github/silentdth/HttpTwo?svg=true)](https://ci.appveyor.com/project/silentdth/httptwo)

It's currently not very well tested and lacks some implementation details.

**What's working:**
 - All frame types can be parsed and can be generated to send to a stream
 - HPack for frames that send headers (thanks to @ringostarr80)
 - Flow control is implemented in theory but not well tested
 - WINDOW_UPDATEs on the connection level (not yet per stream)
 - Simple requests should work
  
**What's not working / not implemented:**
 - Missing sending of WINDOW_UPDATE frames for streams, but streams aren't very long lived yet so it shouldn't be too bad - except for when receiving large downloads/data sets from a request
 - No optimizations for large amounts of data sent/received (there may be a lot of request/response data that gets processed completely in memory currently)
 - Secure connections require TLS 1.2 according to the RFC so they won't work on Mono at this point
 - No ALPN support (see below)
 - Stream prioritization isn't implemented (you can still send Priority frames)
 - Push Promise isn't implemented, but you can tell the server it is disabled
 - Much more test coverage needed
 - No Upgrade support (only starting with prior knowledge in clear text)
 - HttpClient Support (Http2MessageHandler)
   - Passes along all headers - might not make sense always

**Reason for the Lack of ALPN Support**

The HTTP/2 RFC states that secure connections must use ALPN to negotiate the protocol.  Unfortunately, .NET's `SslStream` has no ability to specify application protocols as part of the TLS authentication, so it can't support ALPN.  There's an [issue tracking this on dotnetfx](https://github.com/dotnet/corefx/issues/4721) however it seems like this isn't going to happen very soon (especially on mono and .NET 4.x).

In practice, Apple does not enforce this ALPN negotiation on their APNS HTTP/2 servers, and given that they seem to use Netty, it's possible other servers will not require this either.

Not much I can do about this currently.


### Using the Http2Client

There's a `Http2Client` class that can be used to send requests and receive responses:

```csharp
// Uri to request
var uri = new Uri ("http://somesite.com:80/index.html");

// Create a Http2Client
var http2 = new Http2Client (uri);

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

There aren't very many tests right now, however all the tests require a local `node-http2` server to be setup to run them.  To make this easier, the source is included in `HttpTwo.Tests/node-http2`.  Make sure you run `npm install` in that folder after check out before you start the NUnit tests (which will automatically launch the node server when you run the tests, and stop it when the tests have completed).

### License

Copyright 2015-2016 Jonathan Dick

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
