using System;
using System.Net;
using System.Net.Sockets;

namespace CSSockets.Tcp
{
    public class Listener
    {
        private readonly object sync = new object();
        public SocketWrapper _base = null;
        public SocketWrapper Base => (_base?.State == WrapperState.Destroyed ? Create() : _base) ?? Create();

        private SocketWrapper Create()
        {
            IPEndPoint endPoint = _base?.Local;
            _base = new SocketWrapper();
            _base.WrapperBind();
            _base.WrapperAddServer(this);
            _base.ServerExclusive = Exclusive;
            _base.ServerBacklog = Backlog;
            _base.WrapperOnSocketError = _OnError;
            _base.ServerOnConnection = _OnConnection;
            if (endPoint != null) _base.ServerLookup(endPoint);
            return _base;
        }

        public bool Exclusive { get; set; } = SocketWrapper.SERVER_EXCLUSIVE;
        public int Backlog { get; set; } = SocketWrapper.SERVER_BACKLOG;
        public bool Bound => _base.State >= WrapperState.ServerBound;
        public bool Listening => _base.State == WrapperState.ServerListening;
        public EndPoint BindEndPoint
        {
            get => _base.Local;
            set => _base.ServerLookup(value ?? throw new ArgumentNullException(nameof(value)));
        }

        public event ConnectionHandler OnConnection;
        public event SocketErrorHandler OnError;

        public Listener() => Create();
        public Listener(EndPoint endPoint) : this() => BindEndPoint = endPoint;

        private void _OnError(SocketError error) => OnError?.Invoke(error);
        private void _OnConnection(Connection newConnection) => OnConnection?.Invoke(newConnection);

        public void Start()
        {
            lock (sync) _base.ServerListen();
        }
        public void Stop()
        {
            lock (sync) _base.ServerTerminate();
        }
        public void Reset()
        {
            lock (sync) Create();
        }
    }
}
