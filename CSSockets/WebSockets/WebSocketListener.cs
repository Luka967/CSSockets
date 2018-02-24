using System;
using System.Net;
using System.Text;
using CSSockets.Http.Reference;
using CSSockets.Http.Structures;
using System.Security.Cryptography;

namespace CSSockets.WebSockets
{
    public delegate bool ClientVerifierHandler(RequestHead head);
    public delegate void ConnectionHandler(WebSocket newConnection);
    public class WebSocketListener
    {
        public Listener Base { get; }
        public bool Listening => Base.Listening;

        public ClientVerifierHandler ClientVerifier { get; set; }
        public event ConnectionHandler OnConnection;

        public WebSocketListener(EndPoint listenEndpoint) => Base = new Listener(listenEndpoint) { OnRequest = _onRequest };

        private void _onRequest(ClientRequest req, ServerResponse res)
        {
            if (req.Version != "HTTP/1.1")
            {
                // bad http version
                DropRequest(res, 505, "HTTP Version Not Supported", "Use HTTP/1.1");
                return;
            }
            if (req.Method != "GET")
            {
                // bad http version
                DropRequest(res, 405, "Method Not Allowed", "Use GET");
                return;
            }
            if (req["Transfer-Encoding"] != null || req["Content-Encoding"] != null || req["Content-Length"] != null)
            {
                // has body
                DropRequest(res, 400, "Bad Request", "No body allowed");
                return;
            }
            if (req["Connection"] != "Upgrade")
            {
                // not upgrading
                DropRequest(res, 426, "Upgrade Required", "Upgrade required");
                return;
            }
            if (req["Upgrade"] != "websocket")
            {
                // not upgrading to websocket
                DropRequest(res, 400, "Bad Request", "Upgrade to websocket");
                return;
            }
            if (req["Sec-WebSocket-Version"] != "13")
            {
                // bad websocket version
                DropRequest(res, 400, "Bad Request", "Unsupported WebSocket version", new Header("Sec-WebSocket-Version", "13"));
                return;
            }
            if (req["Sec-WebSocket-Key"] == null)
            {
                // Sec-WebSocket-Key not given
                DropRequest(res, 400, "Bad Request", "Sec-WebSocket-Key not given");
                return;
            }
            if (ClientVerifier != null && !ClientVerifier(req.Head))
            {
                // rejected
                DropRequest(res, 403, "Forbidden", "");
                return;
            }

            SHA1 hasher = SHA1.Create();
            byte[] result = hasher.ComputeHash(Encoding.UTF8.GetBytes(req["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
            string str = Convert.ToBase64String(result, Base64FormattingOptions.None);
            hasher.Dispose();

            res.ResponseCode = 101;
            res.ResponseDescription = "Switching Protocols";
            res["Connection"] = "upgrade";
            res["Upgrade"] = "websocket";
            res["Sec-WebSocket-Version"] = "13";
            res["Sec-WebSocket-Accept"] = str;

            byte[] trail = res.Upgrade();
            ServerWebSocket ws = new ServerWebSocket(req.Connection.Base, req.Head);
            OnConnection?.Invoke(ws);
            ws.WriteTrail(trail);
        }

        private void DropRequest(ServerResponse res, ushort code, string reason, string body, params Header[] otherHeaders)
        {
            res.ResponseCode = code;
            res.ResponseDescription = reason;
            res["Content-Length"] = body.Length.ToString();
            res["Connection"] = "close";
            foreach (Header h in otherHeaders) res[h.Name] = h.Value;
            res.Write(body);
            res.End();
            res.Connection.End();
        }

        private static string ToBase16String(byte[] array)
        {
            string s = "";
            for (long i = 0; i < array.LongLength; i++)
                s += array[i].ToString("X2").ToLowerInvariant();
            return s;
        }

        public void Start() => Base.Start();
        public void Stop() => Base.Stop();
    }
}
