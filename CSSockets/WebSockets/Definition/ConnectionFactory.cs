using System.Net;
using CSSockets.Tcp;
using CSSockets.Http.Reference;
using CSSockets.Http.Definition;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CSSockets.WebSockets.Definition
{
    public abstract class ConnectionFactory<TConnection> : Negotiator where TConnection : Connection
    {
        protected override IEnumerable<NegotiatingExtension> RespondExtensions(NegotiatingExtension[] requested) => null;

        public virtual TConnection Generate(Tcp.Connection connection, URL url, params string[] reqSubprotocols)
        {
            ClientConnection httpClient = new ClientConnection(connection);
            OutgoingRequest req = httpClient.Enqueue("HTTP/1.1", "GET", url);
            TConnection newConnection = GenerateConnection(connection, req.Head);

            string reqExtensions = NegotiatingExtension.Stringify(RequestExtensions());
            string key = Secret.GenerateKey();

            req["Connection"] = "Upgrade";
            req["Upgrade"] = "websocket";
            req["Sec-WebSocket-Version"] = "13";
            req["Sec-WebSocket-Key"] = key;
            if (reqSubprotocols.Length > 0) req["Sec-WebSocket-Protocol"] = SubprotocolNegotiation.Stringify(reqSubprotocols);
            if (reqExtensions.Length > 0) req["Sec-WebSocket-Extensions"] = reqExtensions;

            req.OnResponse += (res) =>
            {
                if (!VerifyResponseHead(res, key)) { httpClient.Terminate(); return; }
                string subprotocol = res["Sec-WebSocket-Protocol"];
                if (subprotocol != null)
                {
                    bool validSubprotocol = false;
                    for (int i = 0; i < reqSubprotocols.Length; i++)
                        if (reqSubprotocols[i] == subprotocol) { validSubprotocol = true; break; }
                    if (!validSubprotocol) { httpClient.Terminate(); return; }
                }
                newConnection.SetSubprotocol(subprotocol);
                if (
                    !NegotiatingExtension.TryParse(res["Sec-WebSocket-Extensions"] ?? "", out NegotiatingExtension[] resExtensions)
                 || !CheckExtensions(resExtensions)
                   ) { httpClient.Terminate(); return; }

                byte[] trail = httpClient.Freeze();
                if (!httpClient.End()) return;
                newConnection.Initiate(trail);
            };

            return req.End() ? newConnection : null;
        }
        protected abstract TConnection GenerateConnection(Tcp.Connection connection, RequestHead req);
        protected virtual bool VerifyResponseHead(IncomingResponse res, string key)
        {
            if (res.StatusCode != 101) return false;
            if (res["Connection"] != "Upgrade") return false;
            if (res["Upgrade"] != "websocket") return false;
            if (res["Sec-WebSocket-Version"] != "13") return false;
            if (res["Sec-WebSocket-Accept"] != Secret.ComputeAccept(key)) return false;
            return true;
        }
    }
}
