using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    public class ClientRequest : IncomingMessage<RequestHead, ResponseHead>
    {
        public ClientRequest(Connection<RequestHead, ResponseHead> connection, RequestHead head) : base(connection, head) { }

        // head accesors
        public Query Query => Head.Query;
        public string Method => Head.Method;
    }

    public class ServerResponse : OutgoingMessage<RequestHead, ResponseHead>
    {
        public ServerResponse(Connection<RequestHead, ResponseHead> connection) : base(connection) { }

        // head accesors
        public ushort StatusCode
        {
            get => Head.StatusCode;
            set { ThrowIfHeadSent(); Head.StatusCode = value; }
        }
        public string StatusDescription
        {
            get => Head.StatusDescription;
            set { ThrowIfHeadSent(); Head.StatusDescription = value; }
        }

        public void SetHead(ushort statusCode, string statusDescription, params Header[] headers)
        {
            ThrowIfHeadSent();
            Head.StatusCode = statusCode;
            Head.StatusDescription = statusDescription;
            foreach (Header h in headers)
                Head.Headers[h.Name] = h.Value;
        }
    }
}
