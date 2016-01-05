# HttpTwo

A basic C# HTTP/2 client library implementation

The focus of this library is to bring enough HTTP/2 functionality to .NET for implementing the APNS (Apple Push Notification Service) provider API over HTTP/2 within [PushSharp](https://github.com/redth/pushsharp)

It's currently very untested and only partially implemented.

**What's working:**
 - All frame types can be parsed and can be generated to send to a stream
 - HPack for frames that send headers

**What's not working / not implemented:**
 - Secure connections require TLS 1.2 according to the RFC so they won't work on Mono at this point
 - No flow control is in place
 - Stream priorities aren't implemented
 - Push Promise isn't implemented
 - Much more test coverage needed

