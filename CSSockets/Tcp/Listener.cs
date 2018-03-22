using System;
using System.Net;
using System.Net.Sockets;
using CSSockets.Tcp.Wrap;

namespace CSSockets.Tcp
{
    public class Listener
    {
        private readonly object _sync = new object();
        public SocketWrapper Base { get; private set; }

        private SocketWrapper Create()
        {
            IPEndPoint endPoint = Base?.Local;
            Base = new SocketWrapper();
            Base.WrapperBind();
            Base.WrapperAddServer(this);
            Base.ServerExclusive = Exclusive;
            Base.ServerBacklog = Backlog;
            Base.WrapperOnSocketError = _OnError;
            Base.ServerOnConnection = _OnConnection;
            if (endPoint != null) Base.ServerLookup(endPoint);
            return Base;
        }

        public bool Exclusive { get; set; } = SocketWrapper.SERVER_EXCLUSIVE;
        public int Backlog { get; set; } = SocketWrapper.SERVER_BACKLOG;
        public bool Bound => Base.State >= WrapperState.ServerBound;
        public bool Listening => Base.State == WrapperState.ServerListening;
        public EndPoint BindEndPoint
        {
            get => Base.Local;
            set => Base.ServerLookup(value ?? throw new ArgumentNullException(nameof(value)));
        }

        public event ConnectionHandler OnConnection;
        public event SocketErrorHandler OnError;

        public Listener() => Create();
        public Listener(EndPoint endPoint) : this() => BindEndPoint = endPoint;

        private void _OnError(SocketError error)
            => OnError?.Invoke(error);
        private void _OnConnection(Connection newConnection)
            => OnConnection?.Invoke(newConnection);

        public void Start()
        {
            lock (_sync) Base.ServerListen();
        }
        public void Stop()
        {
            lock (_sync) Base.ServerTerminate();
        }
        public void Reset()
        {
            lock (_sync) Create();
        }
    }
}
