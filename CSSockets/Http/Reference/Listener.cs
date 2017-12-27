using System;
using System.Net;
using CSSockets.Tcp;
using CSSockets.Http.Base;
using CSSockets.Http.Primitives;
using System.Collections.Generic;

namespace CSSockets.Http.Reference
{
    public delegate void ServerRequestHandler(ClientRequest req, ServerResponse res);
    public delegate void ServerConnectionHandler(ServerConnection newConnection);
    public class Listener
    {
        public EndPoint ListenEndpoint { get; }
        public HttpPath ListenPath { get; }
        public TcpListener Base { get; }
        public bool Listening => Base.Listening;

        public HashSet<ServerConnection> Connections { get; }
        private object Sync { get; } = new object();
        private ServerRequestHandler handler = (req, res) => req.End();
        private HttpMessageHandler<RequestHead, ResponseHead> transformer;

        public event ServerConnectionHandler OnConnection;
        public ServerRequestHandler OnRequest
        {
            get => handler;
            set
            {
                if (Listening) throw new InvalidOperationException("Cannot change OnRequest handler while listening");
                handler = value;
            }
        }

        private Listener()
        {
            transformer = (_req, _res) =>
            {
                ClientRequest req = _req as ClientRequest;
                ServerResponse res = _res as ServerResponse;
                if (!req.Query.Path.Contains(ListenPath)) return;
                handler(req, res);
            };
            Base = new TcpListener();
            Base.OnConnection += OnNewSocket;
            Connections = new HashSet<ServerConnection>();
        }

        public Listener(EndPoint listenEndpoint) : this()
        {
            ListenEndpoint = listenEndpoint;
            ListenPath = "/";
            Base.Bind(listenEndpoint);
        }

        public Listener(EndPoint listenEndpoint, HttpPath listenPath) : this()
        {
            ListenEndpoint = listenEndpoint;
            ListenPath = listenPath;
            Base.Bind(listenEndpoint);
        }

        private void OnNewSocket(TcpSocket socket)
        {
            ServerConnection conn = new ServerConnection(socket);
            socket.OnClose += () => { lock (Sync) Connections.Remove(conn); };
            lock (Sync) Connections.Add(conn);
            OnConnection?.Invoke(conn);
            conn.OnMessage = (req, res) => handler(req as ClientRequest, res as ServerResponse);
        }

        public void Start()
        {
            if (handler == null) throw new InvalidOperationException("Cannot start listening when no request handler is set");
            Base.Start();
        }
        public void Stop()
        {
            Base.Stop();
            lock (Sync)
            {
                ServerConnection[] enumSafeList = new ServerConnection[Connections.Count];
                Connections.CopyTo(enumSafeList);
                foreach (ServerConnection conn in enumSafeList)
                    conn.Terminate();
            }
        }
    }
}
