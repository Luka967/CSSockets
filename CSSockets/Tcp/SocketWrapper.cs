using System;
using System.Net;
using CSSockets.Streams;
using System.Net.Sockets;

namespace CSSockets.Tcp
{
    public class SocketWrapper
    {
        public const bool SERVER_EXCLUSIVE = true;
        public const int SERVER_BACKLOG = 511;

        public Socket Socket { get; }
        internal IOThread BoundThread = null;
        public WrapperState State { get; internal set; } = WrapperState.Unset;
        public IPEndPoint Local { get; internal set; } = null;
        public IPEndPoint Remote { get; internal set; } = null;
        public bool EndedReadable { get; internal set; } = false;
        public bool EndedWritable { get; internal set; } = false;

        public SocketWrapper() : this(new Socket(SocketType.Stream, ProtocolType.Tcp)) { }
        public SocketWrapper(Socket socket)
        {
            Socket = socket;
            Socket.Blocking = false;
            Socket.NoDelay = true;
        }

        public void WrapperBind()
            => (BoundThread = IOControl.GetBest()).Enqueue(new IOOperation()
            {
                Callee = this,
                Type = OperationType.WrapperBind,
            });
        public void WrapperAddClient(Connection connection)
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Connection = connection,
                Type = OperationType.WrapperAddClient,
            });
        public void WrapperAddServer(Listener listener)
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Listener = listener,
                Type = OperationType.WrapperAddServer,
            });

        public void ServerLookup(EndPoint endPoint)
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Lookup = endPoint,
                Type = OperationType.ServerLookup,
            });
        public void ServerListen()
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Type = OperationType.ServerListen,
            });
        public void ServerTerminate()
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Type = OperationType.ServerTerminate,
            });

        public void ClientConnect(EndPoint endPoint)
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Lookup = endPoint,
                Type = OperationType.ClientConnect,
            });
        public void ClientOpen(SocketWrapper referer, Connection connection)
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Referer = referer,
                Connection = connection,
                Type = OperationType.ClientOpen,
            });
        public void ClientShutdown()
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Type = OperationType.ClientShutdown,
            });
        public void ClientTerminate()
            => BoundThread.Enqueue(new IOOperation()
            {
                Callee = this,
                Type = OperationType.ClientTerminate,
            });

        public SocketCodeHandler WrapperOnSocketError { get; set; }
        public ControlHandler WrapperOnUnbind { get; set; }

        public ConnectionHandler ServerOnConnection { get; set; }

        public ControlHandler ClientOnConnect { get; set; }
        public ControlHandler ClientOnTimeout { get; set; }
        public ControlHandler ClientOnShutdown { get; set; }
        public ControlHandler ClientOnClose { get; set; }

        public int ServerBacklog { get; set; } = SERVER_BACKLOG;
        public bool ServerExclusive { get; set; } = SERVER_EXCLUSIVE;

        public bool ClientAllowHalfOpen { get; set; } = false;
        public DateTime ClientLastActivity { get; internal set; } = DateTime.UtcNow;
        public TimeSpan? ClientTimeoutAfter { get; set; } = null;
        public bool ClientCalledTimeout { get; internal set; } = false;
    }
}
