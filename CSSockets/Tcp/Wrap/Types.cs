using System.Net;
using System.Net.Sockets;

namespace CSSockets.Tcp.Wrap
{
    public delegate void SocketErrorHandler(SocketError error);
    public delegate void ConnectionHandler(Connection newConnection);

    public enum WrapperType : byte
    {
        Unset = 0,
        Server = 1,
        Client = 2
    }
    public enum WrapperState : byte
    {
        Unset = 0,                  // no owner
        Dormant = 1,                // no type
        ServerWaitBind = 2,         // server, no bind
        ServerBound = 3,            // server, has bind
        ServerListening = 4,        // server, listening
        ClientDormant = 5,          // client, no socket
        ClientConnecting = 6,       // client, has socket, is connecting
        ClientOpen = 7,             // client, has socket, can read/write
        ClientReadonly = 8,         // client, has socket, can only read
        ClientWriteonly = 9,        // client, has socket, can only write
        ClientLastWrite = 10,       // client, has socket, writing from buffer
        ClientClosed = 11,          // client, waiting for close
        Destroyed = 12              // nothing available
    }

    public enum IOOperationType : byte
    {
        Noop = 0,
        WrapperBind = 1,
        WrapperAddServer = 2,
        WrapperAddClient = 3,
        ServerLookup = 4,
        ServerListen = 5,
        ServerTerminate = 6,
        ClientConnect = 7,
        ClientShutdown = 8,
        ClientTerminate = 9
    }
    public struct IOOperation
    {
        public Socket Socket => Callee.Socket;
        public SocketWrapper Callee { get; set; }
        public IOOperationType Type { get; set; }
        public EndPoint Lookup { get; set; }
        public Connection User_1 { get; set; }
        public Listener User_2 { get; set; }
        public WrapperState AdvanceFrom => Callee.State;
        public WrapperState AdvanceTo { get; set; }
        public WrapperState FailAdvanceTo { get; set; }
        public WrapperState BrokenAdvanceTo { get; set; }
    }
}
