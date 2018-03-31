using System;
using System.Net;
using System.Text;
using CSSockets.Http.Reference;
using CSSockets.Http.Structures;
using System.Security.Cryptography;

namespace CSSockets.WebSockets
{
    public delegate bool ClientVerifierHandler(ClientRequest req);
    public delegate void ConnectionHandler(WebSocket newConnection);
    public class WebSocketListener
    {
        public Listener Base { get; }
        public bool Listening => Base.Listening;

        public ClientVerifierHandler ClientVerifier { get; set; } = null;
        public event ConnectionHandler OnConnection;

        public WebSocketListener(EndPoint listenEndpoint) => Base = new Listener(listenEndpoint) { OnRequest = _upgrade };
        public WebSocketListener(Listener listener) => Base = listener;

        private void _upgrade(ClientRequest req, ServerResponse res) => Upgrade(req, res);
        public bool Upgrade(ClientRequest req, ServerResponse res)
        {
            if (req.Version != "HTTP/1.1")
                return dropRequest(res, 505, "HTTP Version Not Supported", "Use HTTP/1.1");
            if (req.Method != "GET")
                return dropRequest(res, 405, "Method Not Allowed", "Use GET");
            if (req["Transfer-Encoding"] != null || req["Content-Encoding"] != null || req["Content-Length"] != null)
                return dropRequest(res, 400, "Bad Request", "No body allowed");

            if (req["Connection"] != "Upgrade")
                return dropRequest(res, 426, "Upgrade Required", "Upgrade required");
            if (req["Upgrade"] != "websocket")
                return dropRequest(res, 400, "Bad Request", "Upgrade to websocket");

            if (req["Sec-WebSocket-Version"] != "13")
                return dropRequest(res, 400, "Bad Request", "Unsupported WebSocket version", new Header("Sec-WebSocket-Version", "13"));
            if (req["Sec-WebSocket-Key"] == null)
                return dropRequest(res, 400, "Bad Request", "Sec-WebSocket-Key not given");

            if (!ClientVerifier?.Invoke(req) ?? false)
                return dropRequest(res, 403, "Forbidden", "");

            SHA1 hasher = SHA1.Create();
            byte[] result = hasher.ComputeHash(Encoding.UTF8.GetBytes(req["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
            string str = Convert.ToBase64String(result, Base64FormattingOptions.None);
            hasher.Dispose();

            res.ResponseCode = 101;
            res.ResponseDescription = "Switching Protocols";
            res["Connection"] = "Upgrade";
            res["Upgrade"] = "websocket";
            res["Sec-WebSocket-Version"] = "13";
            res["Sec-WebSocket-Accept"] = str;

            byte[] trail = res.Upgrade();
            ServerWebSocket ws = new ServerWebSocket(req.Connection.Base, req.Head);
            OnConnection?.Invoke(ws);
            ws.WriteTrail(trail);
            return true;
        }

        private bool dropRequest(ServerResponse res, ushort code, string reason, string body, params Header[] otherHeaders)
        {
            res.ResponseCode = code;
            res.ResponseDescription = reason;
            res["Content-Length"] = body.Length.ToString();
            res["Connection"] = "close";
            foreach (Header header in otherHeaders) res[header.Name] = header.Value;
            res.Write(body);
            res.End();
            res.Connection.End();
            return false;
        }

        public void Start() => Base.Start();
        public void Stop() => Base.Stop();
    }
}
