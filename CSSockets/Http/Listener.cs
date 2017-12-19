using System;
using System.Net;
using CSSockets.Tcp;
using System.Collections.Generic;

namespace CSSockets.Http
{
    public delegate void ServerRequestHandler(HttpClientRequest req, HttpServerResponse res);
    public delegate void ServerConnectionHandler(HttpServerConnection newConnection);
    public class HttpListener
    {
        public EndPoint ListenEndpoint { get; }
        public Path ListenPath { get; }
        public TcpListener Base { get; }
        public bool Listening => Base.Listening;

        public HashSet<HttpServerConnection> Connections { get; }
        private object Sync { get; } = new object();
        private ServerRequestHandler handler = (req, res) => req.End();
        private HttpMessageHandler<HttpRequestHead, HttpResponseHead> transformer;

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

        private HttpListener()
        {
            transformer = (_req, _res) =>
            {
                HttpClientRequest req = _req as HttpClientRequest;
                HttpServerResponse res = _res as HttpServerResponse;
                if (!req.Query.Path.Contains(ListenPath)) return;
                handler(req, res);
            };
            Base = new TcpListener();
            Base.OnConnection += OnNewSocket;
            Connections = new HashSet<HttpServerConnection>();
        }

        public HttpListener(EndPoint listenEndpoint) : this()
        {
            ListenEndpoint = listenEndpoint;
            ListenPath = "/";
            Base.Bind(listenEndpoint);
        }

        public HttpListener(EndPoint listenEndpoint, Path listenPath) : this()
        {
            ListenEndpoint = listenEndpoint;
            ListenPath = listenPath;
            Base.Bind(listenEndpoint);
        }

        private void OnNewSocket(TcpSocket socket)
        {
            HttpServerConnection conn = new HttpServerConnection(socket);
            lock (Sync) Connections.Add(conn);
            conn.OnEnd += () => { lock (Sync) Connections.Remove(conn); };
            OnConnection?.Invoke(conn);
            conn.OnMessage = (req, res) => handler(req as HttpClientRequest, res as HttpServerResponse);
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
                HttpServerConnection[] enumSafeList = new HttpServerConnection[Connections.Count];
                Connections.CopyTo(enumSafeList);
                foreach (HttpServerConnection conn in enumSafeList)
                    conn.Terminate();
            }
        }
    }
}
