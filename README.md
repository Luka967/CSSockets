# CSSockets

An implementation of event-based sockets for .NET Core 2.0.
Includes highly scalable, wrapped TCP, low-level HTTP and raw WebSockets, all being thread-safe, yet internally use the least amount of threads.
Data handling is done with Node.js-inspired reinvented streams - see CSSockets.Streams.
This is a shaky but functioning library - bug hunting is encouraged.

This project uses object-oriented programming to a large extent to enable heavy customization:
  - All streams inherit either interfaces directly or base classes.
  - Sockets are wrapped then accessed with events and stream methods however it's left exposed if you want to do magic.
  - All the base HTTP classes are built on generics so you can even make your own HTTP version, albeit not in the form of HTTP/2.

The performance focus is around parallelization of heavy workloads but with minimal cross-thread tampering:
  - Calls to Readable, Writable, Duplex and Compressors stream implemenatations don't cross or make new threads.
  - Accepted sockets made by CSSockets.Tcp.Listener are handled with Socket.Select to ensure minimal thread usage.
  - Multiple socket I/O processor threads will be opened for more than X sockets - even that is open to change.

Implementations:

- [X] Base stream classes
  - [X] Readable
  - [X] Writable
  - [X] Duplex
  - [X] Transform (UnifiedDuplex is capable of this)
- [X] TCP
  - [X] Client
  - [X] Server
- [X] HTTP
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
    - [X] Client-side connection
    - [X] Server-side connection
  - [X] Support for custom upgrading
  - [X] HTTP listener
- [X] WebSockets
  - [X] Message parsing
  - [X] Message serialization
  - [X] WebSocket connections
    - [X] Client-side connection
    - [X] Server-side connection
  - [X] Support for custom extensions
    - Implement permessage-deflate?
- HTTPS?
- Secure WebSockets?
