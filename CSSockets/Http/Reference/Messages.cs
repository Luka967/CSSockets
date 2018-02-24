using System;
using CSSockets.Streams;
using CSSockets.Http.Base;
using CSSockets.Http.Structures;

namespace CSSockets.Http.Reference
{
    public class ClientRequest : Request<RequestHead, ResponseHead>
    {
        public ClientRequest(RequestHead head, BodyType bodyType, ServerConnection connection) : base(head, bodyType, connection) { }
        public string Method => Head.Method;
        public Query Query => Head.Query;
        public Path Path => Head.Query.Path;
    }

    public class ServerResponse : Response<RequestHead, ResponseHead>
    {
        public ServerResponse(Structures.Version version, ServerConnection connection)
            : base(version, connection) { }
        public ushort ResponseCode
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.StatusCode.Value;
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.StatusCode = value; }
        }
        public string ResponseDescription
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.StatusDescription;
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.StatusDescription = value; }
        }

        public override bool SendHead()
        {
            if (!Connection.SendHead(head)) return false;
            return IsHeadSent = true;
        }
        public override bool End() => base.End() && Connection.FinishResponse();
        public byte[] Upgrade()
        {
            if (!base.End() || !Connection.FinishResponse()) return null;
            if (!Connection.Detach()) return null;
            byte[] a = Connection.HeadParser.Read();
            byte[] b = Connection.BodyParser.Excess.Read();
            byte[] c = new byte[a.LongLength + b.LongLength];
            PrimitiveBuffer.Copy(a, 0, c, 0, (ulong)a.LongLength);
            PrimitiveBuffer.Copy(b, (ulong)a.LongLength, c, 0, (ulong)b.LongLength);
            Connection.Abandon();
            return c;
        }
    }
}
