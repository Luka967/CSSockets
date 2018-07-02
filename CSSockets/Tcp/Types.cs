using System.Net;
using System.Net.Sockets;

namespace CSSockets.Tcp
{
    public delegate void SocketCodeHandler(SocketError error);
    public delegate void ConnectionHandler(Connection newConnection);

    public enum WrapperState : byte
    {
        Unset,                       // no owner
        Dormant,                     // no type
        ServerDormant,               // server, no bind
        ServerBound,                 // server, has bind
        ServerListening,             // server, listening
        ClientDormant,               // client, no socket
        ClientConnecting,            // client, has socket, is connecting
        ClientOpen,                  // client, has socket, can read/write
        ClientReadonly,              // client, has socket, can only read
        ClientWriteonly,             // client, has socket, can only write
        ClientLastWrite,             // client, has socket, emptyin buffer
        Destroyed                    // nothing available
    }

    public enum OperationType : byte
    {
        Noop = 0,
        WrapperBind = 1,
        WrapperAddServer = 2,
        WrapperAddClient = 3,
        ServerLookup = 4,
        ServerListen = 5,
        ServerTerminate = 6,
        ClientOpen = 7,
        ClientConnect = 8,
        ClientShutdown = 9,
        ClientTerminate = 10
    }
    public struct IOOperation
    {
        public Socket Socket => Callee.Socket;
        public SocketWrapper Callee { get; set; }
        public SocketWrapper Referer { get; set; }
        public OperationType Type { get; set; }
        public EndPoint Lookup { get; set; }
        public Connection Connection { get; set; }
        public Listener Listener { get; set; }
        public WrapperState AdvanceFrom => Callee.State;
    }
}
