using CSSockets.Streams;
using CSSockets.Http.Definition;

namespace CSSockets.Http.Reference
{
    public sealed class IncomingRequest : IncomingMessage<RequestHead, ResponseHead>
    {
        public new ServerConnection Connection => base.Connection as ServerConnection;

        public URL URL => Head.URL;
        public Path Path => Head.URL.Path;
        public string Hash => Head.URL.Hash;
        public string Method => Head.Method;
        public Queries Queries => Head.URL.Queries;

        public IncomingRequest(ServerConnection connection, RequestHead head) : base(connection, head) { }
    }
    public sealed class IncomingResponse : IncomingMessage<ResponseHead, RequestHead>
    {
        public new ClientConnection Connection => base.Connection as ClientConnection;

        public ushort StatusCode => Head.StatusCode;
        public string StatusDescription => Head.StatusDescription;

        public IncomingResponse(ClientConnection connection, ResponseHead head) : base(connection, head) { }
    }

    public delegate void IncomingResponseHandler(IncomingResponse response);
    public sealed class OutgoingRequest : OutgoingMessage<ResponseHead, RequestHead>
    {
        public new ClientConnection Connection => base.Connection as ClientConnection;

        public string Method
        {
            get { lock (Sync) return Head.Method; }
            set { lock (Sync) { if (SentHead) return; Head.Method = value; } }
        }
        public URL URL
        {
            get { lock (Sync) return Head.URL; }
            set { lock (Sync) { if (SentHead) return; Head.URL = value; } }
        }

        public bool Responded { get; private set; } = false;
        public event IncomingResponseHandler OnResponse;
        internal bool FireResponse(IncomingResponse response)
        {
            lock (Sync)
            {
                if (Responded) return false;
                OnResponse?.Invoke(response);
                return Responded = true;
            }
        }
        
        public OutgoingRequest(ClientConnection connection, Version version, string method, URL url) : base(connection, version)
        {
            Method = method;
            URL = url;
        }
    }
    public sealed class OutgoingResponse : OutgoingMessage<RequestHead, ResponseHead>
    {
        public new ServerConnection Connection => base.Connection as ServerConnection;

        public ushort StatusCode
        {
            get { lock (Sync) return Head.StatusCode; }
            set { lock (Sync) { if (SentHead) return; Head.StatusCode = value; } }
        }
        public string StatusDescription
        {
            get { lock (Sync) return Head.StatusDescription; }
            set { lock (Sync) { if (SentHead) return; Head.StatusDescription = value; } }
        }

        public OutgoingResponse(ServerConnection connection, Version version) : base(connection, version) { }

        public bool SendHead(ushort statusCode, string statusDescription)
        {
            lock (Sync)
            {
                if (SentHead) return false;
                Head.StatusCode = statusCode;
                Head.StatusDescription = statusDescription;
                return SendHead();
            }
        }
        public bool End(ushort statusCode, string statusDescription)
        {
            lock (Sync)
            {
                if (SentHead) return false;
                Head.StatusCode = statusCode;
                Head.StatusDescription = statusDescription;
                return End();
            }
        }
    }
}
