# CSSockets

A WIP implementation of barebones TCP, capable HTTP and fully-featured WebSockets using custom Node.js-like streams and independent implementations for various classes. The performance focus is around parallelization but with minimal cross-thread tampering.

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
  - [ ] Body parsing
    - [ ] Binary
    - [ ] Chunked
    - [ ] Compressed binary
    - [ ] Compressed chunked
  - [ ] Body serializing
    - [ ] Binary
    - [ ] Chunked
    - [ ] Compressed binary
    - [ ] Compressed chunked
  - [ ] Request handling
  - [ ] Response handling
  - [ ] HTTP connections
    - [ ] Client-side connection
    - [ ] Server-side connection
  - [ ] Support for custom upgrading
  - [ ] HTTP listener
- [ ] WebSockets
  - [ ] Message parsing
  - [ ] Message serialization
  - [ ] WebSocket connections
    - [ ] Client-side connection
    - [ ] Server-side connection
  - [ ] Support for custom extensions
    - Implement permessage-deflate?
- HTTPS?
- Secure WebSockets?