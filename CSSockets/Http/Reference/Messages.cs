using System;
using CSSockets.Streams;
using CSSockets.Http.Base;
using CSSockets.Http.Structures;

namespace CSSockets.Http.Reference
{
    public class IncomingRequest : IncomingMessage<RequestHead, ResponseHead>
    {
        public IncomingRequest(RequestHead head, BodyType bodyType, ServerConnection connection) : base(head, bodyType, connection) { }
        public string Method => Head.Method;
        public Query Query => Head.Query;
        public Path Path => Head.Query.Path;
    }

    public class OutgoingResponse : OutgoingMessage<RequestHead, ResponseHead>
    {
        public new ServerConnection Connection => base.Connection as ServerConnection;
        public OutgoingResponse(Structures.Version version, ServerConnection connection) : base(version, connection) { }
        public ushort StatusCode
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.StatusCode.Value;
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.StatusCode = value; }
        }
        public string StatusDescription
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.StatusDescription;
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.StatusDescription = value; }
        }
        public bool SendContinue()
        {
            if (!Connection.SendContinue()) return false;
            return IsContinueSent = true;
        }
        public byte[] Upgrade()
        {
            if (!base.End() || !Connection.FinishResponse()) return null;
            if (!Connection.Freeze()) return null;
            byte[] a = new byte[Connection.HeadParser.Buffered];
            byte[] b = new byte[Connection.BodyParser.ExcessBuffered];
            Connection.HeadParser.Read(a);
            Connection.BodyParser.Excess.Read(b);
            byte[] c = new byte[a.LongLength + b.LongLength];
            PrimitiveBuffer.Copy(a, 0, c, 0, (ulong)a.LongLength);
            PrimitiveBuffer.Copy(b, (ulong)a.LongLength, c, 0, (ulong)b.LongLength);
            Connection.Abandon();
            return c;
        }
    }
}
