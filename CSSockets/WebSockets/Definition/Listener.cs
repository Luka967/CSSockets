using System.Net;
using System.Text;
using CSSockets.Http.Reference;
using CSSockets.Http.Definition;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CSSockets.WebSockets.Definition
{
    public delegate bool ClientVerifier(IPAddress remote, string[] subprotocols, RequestHead head);
    public delegate string SubprotocolChooser(IPAddress remote, string[] subprotocols, RequestHead head);
    public abstract class Listener
    {
        public Http.Reference.Listener Base { get; }

        protected readonly object Sync = new object();
        protected SHA1 Hasher;
        protected ClientVerifier ClientVerifier = null;
        protected SubprotocolChooser SubprotocolChooser = null;

        public Listener()
        {
            Base = new Http.Reference.Listener();
            Base.OnRequest = _RequestHandler;
        }
        public Listener(EndPoint endPoint)
        {
            Base = new Http.Reference.Listener(endPoint);
            Base.OnRequest = _RequestHandler;
        }
        public Listener(Tcp.Listener listener)
        {
            Base = new Http.Reference.Listener(listener);
            Base.OnRequest = _RequestHandler;
        }
        public Listener(Http.Reference.Listener listener)
        {
            Base = new Http.Reference.Listener();
            Base.OnRequest = _RequestHandler;
        }

        protected void _RequestHandler(IncomingRequest req, OutgoingResponse res) => RequestHandler(req, res);
        protected virtual bool RequestHandler(IncomingRequest req, OutgoingResponse res)
        {
            if (!CheckHeaders(req, res)) return false;
            IPAddress remote = res.Connection.Base.RemoteAddress;

            if (!SubprotocolNegotiation.TryParse(req["Sec-WebSocket-Protocol"] ?? "", out string[] subprotocols))
                return DropRequest(res, 400, "Bad Request", "Could not parse subprotocols");
            if (ClientVerifier != null && !ClientVerifier(remote, subprotocols, req.Head))
                return DropRequest(res, 403, "Forbidden");
            if (!NegotiatingExtension.TryParse(req["Sec-WebSocket-Extensions"] ?? "", out NegotiatingExtension[] requestedExtensions))
                return DropRequest(res, 400, "Bad Request", "Could not parse extensions");
            if (!CheckExtensions(requestedExtensions))
                return DropRequest(res, 400, "Bad Request", "Extensions have been rejected");

            if (subprotocols.Length > 0 && SubprotocolChooser != null)
                res["Sec-WebSocket-Protocol"] = SubprotocolChooser(remote, subprotocols, req.Head);
            string respondedExtensions = NegotiatingExtension.Stringify(RespondExtensions(requestedExtensions));
            if (respondedExtensions.Length > 0)
                res["Sec-WebSocket-Extensions"] = respondedExtensions;

            byte[] acceptBinary = Hasher.ComputeHash(Encoding.UTF8.GetBytes(req["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
            string acceptStringified = System.Convert.ToBase64String(acceptBinary, System.Base64FormattingOptions.None);

            res["Connection"] = "Upgrade";
            res["Upgrade"] = "websocket";
            res["Sec-WebSocket-Version"] = "13";
            res["Sec-WebSocket-Accept"] = acceptStringified;
            res.End(101, "Switching Protocols");

            byte[] trail = res.Connection.Freeze();
            if (!res.Connection.End()) return false;
            FireConnection(res.Connection.Base, req.Head, trail);
            return true;
        }
        protected abstract bool CheckExtensions(NegotiatingExtension[] requested);
        protected abstract IEnumerable<NegotiatingExtension> RespondExtensions(NegotiatingExtension[] requested);
        protected abstract void FireConnection(Tcp.Connection connection, RequestHead req, byte[] trail);

        protected bool DropRequest(OutgoingResponse res, ushort code, string reason, string asciiBody = null, params Header[] headers)
        {
            for (int i = 0; i < headers.Length; i++) res[headers[i].Key] = headers[i].Value;
            res["Content-Length"] = asciiBody.Length.ToString();
            res.SendHead(code, reason);
            res.Write(Encoding.ASCII.GetBytes(asciiBody));
            return false;
        }
        protected bool CheckHeaders(IncomingRequest req, OutgoingResponse res)
        {
            if (req.Version != "HTTP/1.1")
                return DropRequest(res, 505, "HTTP Version Not Supported", "Use HTTP/1.1");
            if (req.Method != "GET")
                return DropRequest(res, 405, "Method Not Allowed", "Use GET");

            BodyType? type = BodyType.TryDetectFor(req.Head, true);
            if (type == null)
                return DropRequest(res, 400, "Bad Request", "Cannot detect body type");
            if (type.Value.Encoding != TransferEncoding.None)
                return DropRequest(res, 400, "Bad Request", "No body allowed");

            if (req["Connection"] != "Upgrade")
                return DropRequest(res, 426, "Upgrade Required", "Upgrade required");
            if (req["Upgrade"] != "websocket")
                return DropRequest(res, 400, "Bad Request", "Upgrade to websocket");

            if (req["Sec-WebSocket-Version"] != "13")
                return DropRequest(res, 400, "Bad Request", "Unsupported WebSocket version", new Header("Sec-WebSocket-Version", "13"));
            if (req["Sec-WebSocket-Key"] == null)
                return DropRequest(res, 400, "Bad Request", "Sec-WebSocket-Key not given");
            return true;
        }

        public void Start()
        {
            lock (Sync)
            {
                Hasher = SHA1.Create();
                Base.Start();
            }
        }
        public void Stop()
        {
            lock (Sync)
            {
                Base.Stop();
                Hasher.Dispose();
                Hasher = null;
            }
        }
    }
}
