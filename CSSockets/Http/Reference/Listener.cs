using System.Net;
using CSSockets.Tcp;
using System.Collections.Generic;

namespace CSSockets.Http.Reference
{
    public delegate void ConnectionHandler(ServerConnection connection);
    public sealed class Listener
    {
        private readonly object Sync = new object();
        private readonly HashSet<Connection> Connections = new HashSet<Connection>();

        public Tcp.Listener Base { get; }
        public bool Listening => Base.Listening;
        public EndPoint BindEndPoint
        {
            get => Base.BindEndPoint;
            set => Base.BindEndPoint = value;
        }

        public ConnectionHandler OnConnection { get; set; } = null;
        private IncomingRequestHandler _OnRequest = null;
        public IncomingRequestHandler OnRequest
        {
            get => _OnRequest;
            set => _OnRequest = Listening ? _OnRequest : value;
        }

        public Listener()
        {
            Base = new Tcp.Listener();
            Base.OnConnection += ConnectionHandler;
        }
        public Listener(EndPoint endPoint)
        {
            Base = new Tcp.Listener(endPoint);
            Base.OnConnection += ConnectionHandler;
        }
        public Listener(Tcp.Listener listener)
        {
            Base = listener;
            Base.OnConnection += ConnectionHandler;
        }

        public void Start()
        {
            lock (Sync) Base.Start();
        }
        public void Stop()
        {
            lock (Sync)
            {
                Base.Stop();
                Connection[] connections = new Connection[Connections.Count];
                Connections.CopyTo(connections);
                for (int i = 0; i < connections.Length; i++) connections[i].Terminate();
                Connections.Clear();
            }
        }

        private void ConnectionHandler(Connection connection)
        {
            ServerConnection upgraded;
            lock (Sync)
            {
                if (!Listening) { connection.Terminate(); return; }
                upgraded = new ServerConnection(connection, _OnRequest);
                Connections.Add(connection);
                connection.OnClose += () =>
                {
                    lock (Sync) Connections.Remove(connection);
                };
            }
            OnConnection?.Invoke(upgraded);
        }
    }
}
