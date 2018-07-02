using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Reference;

namespace CSSockets.Http.Definition
{
    public abstract class Connection<TReq, TRes> : IEndable
        where TReq : Head, new() where TRes : Head, new()
    {
        protected readonly object Sync = new object();

        protected HeadParser<TReq> IncomingHead;
        protected HeadSerializer<TRes> OutgoingHead;
        protected BodyParser IncomingBody;
        protected BodySerializer OutgoingBody;

        public virtual event ControlHandler OnEnd;

        public Connection Base { get; }
        public bool Frozen { get; protected set; } = false;
        public bool Ended { get; protected set; } = false;

        public Connection(Connection connection)
        {
            Base = connection;
            Base.OnClose += OnBaseClose;
        }

        protected virtual void OnBaseClose() => End();

        public abstract IncomingMessage<TReq, TRes> Incoming { get; }
        public abstract OutgoingMessage<TReq, TRes> Outgoing { get; }
        public abstract bool StartOutgoing(TRes head);
        public abstract bool FinishOutgoing();

        public abstract byte[] Freeze();
        public virtual bool End()
        {
            lock (Sync)
            {
                if (Ended) return false;
                if (!Frozen) Freeze();
                OnEnd?.Invoke();
                return Ended = true;
            }
        }
        public virtual bool Terminate()
        {
            lock (Sync) return End() && Base.Terminate() && (Ended = true);
        }
    }
}
