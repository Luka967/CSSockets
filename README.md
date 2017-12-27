# CSSockets

A WIP implementation of event-based sockets for .NET Core 2.0.
Includes highly scalable, wrapped TCP, low-level HTTP and raw WebSockets.
Data handling is done with Node.js-inspired reinvented streams - see CSSockets.Streams.
Designed to use the least amount of threads that deliver excellent performance.
Since this is a shaky but functioning library, bug hunting is encouraged. There are basic tests already though.
See the tests (the CSSockets.Program class) if you want to use this library to get a head start.

This project uses object-oriented programming to a large extent to enable heavy customization:
  - All streams inherit either interfaces directly or base classes.
  - TcpSocket is to be wrapped and accessed with events and stream methods, however the Socket behind it is exposed if you want to do magic.
  - All the base HTTP classes are made with generics (see CSSockets.Http.Base and CSSockets.Http.Reference) so you can even make your own HTTP version if you want to.

The performance focus is around parallelization of heavy workloads but with minimal cross-thread tampering:
  - All the BaseReadable, BaseWritable, UnifiedDuplex, BaseDuplex and Compressors stream implemenatations call further operations on the caller thread.
  - Accepted sockets made by CSSockets.Tcp.TcpSocket are handled with Socket.Select to ensure minimal thread usage.
  - Multiple socket I/O processor threads will be opened for more than 32 sockets.

Implementations:

- [X] Base stream classes
  - [X] Readable
  - [X] Writable
  - [X] Duplex
  - [X] Transform (UnifiedDuplex is capable of this)
- [X] TCP
  - [X] Client
  - [X] Server
- [ ] HTTP
  - [X] Base HTTP classes
    - [X] Header
    - [X] Version
    - [X] Query tokens
    - [X] Paths
  - [X] Head parsing
    - [X] Request head
    - [X] Response head
  - [X] Head serializing
    - [X] Request head
    - [X] Response head
  - [X] Body parsing
    - [X] Binary
    - [X] Chunked
    - [X] Compressed binary
    - [X] Compressed chunked
  - [X] Body serializing
    - [X] Binary
    - [X] Chunked
    - [X] Compressed binary
    - [X] Compressed chunked
  - [X] HTTP connections
    - [ ] Client-side connection
    - [X] Server-side connection
  - [X] Support for custom upgrading
  - [X] HTTP listener
- [ ] WebSockets
  - [X] Message parsing
  - [X] Message serialization
  - [X] WebSocket connections
    - [ ] Client-side connection
    - [X] Server-side connection
  - [ ] Support for custom extensions
    - Implement permessage-deflate?
- HTTPS?
- Secure WebSockets?