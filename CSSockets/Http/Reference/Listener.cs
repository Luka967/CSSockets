using System;
using System.Net;
using CSSockets.Tcp;
using System.Collections.Generic;

namespace CSSockets.Http.Reference
{
    public delegate void ListenerConnectionHandler(ServerConnection newConnection);
    public sealed class Listener
    {
        public EndPoint EndPoint { get; }
        public bool Listening => Base.Listening;
        private readonly TcpListener Base;
        private readonly HashSet<ServerConnection> connections = new HashSet<ServerConnection>();
        private readonly object sync = new object();

        public event ListenerConnectionHandler OnConnection;
        private ClientRequestHandler onRequest;
        public ClientRequestHandler OnRequest
        {
            get => onRequest;
            set
            {
                if (Base.Listening) throw new InvalidOperationException("Cannot change OnRequest while listening");
                onRequest = value;
            }
        }

        public Listener(EndPoint endPoint)
        {
            EndPoint = endPoint;
            Base = new TcpListener(endPoint);
            Base.OnConnection += onConnection;
        }

        public void Start() => Base.Start();
        public void Stop()
        {
            Base.Stop();
            lock (sync)
            {
                ServerConnection[] list = new ServerConnection[connections.Count];
                connections.CopyTo(list, 0);
                foreach (ServerConnection conn in list) conn.Terminate();
            }
        }

        private void onConnection(TcpSocket socket)
        {
            ServerConnection connection = new ServerConnection(socket, onRequest);
            connection.OnEnd += () => { lock (sync) connections.Remove(connection); };
            lock (sync)
            {
                if (!Base.Listening) { connection.Terminate(); return; }
                connections.Add(connection);
            }
            OnConnection?.Invoke(connection);
        }
    }
}
