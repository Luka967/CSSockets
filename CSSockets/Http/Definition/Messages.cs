using CSSockets.Streams;

namespace CSSockets.Http.Definition
{
    public abstract class IncomingMessage<TReq, TRes> : MemoryDuplex, IFinishable, IEndable
        where TReq : Head, new() where TRes : Head, new()
    {
        public TReq Head { get; }
        public Connection<TReq, TRes> Connection { get; }

        // Head is supposed to be static, don't lock on Sync
        public Version Version => Head.Version;
        public string this[string key] => Head.Headers[key];

        public virtual event ControlHandler OnFinish;

        public bool Ended { get; private set; } = false;
        public bool Finished { get; private set; } = false;

        public IncomingMessage(Connection<TReq, TRes> connection, TReq head)
        {
            Connection = connection;
            Head = head;
        }

        public virtual bool Finish()
        {
            lock (Sync)
            {
                OnFinish?.Invoke();
                return Finished = true;
            }
        }
        public virtual bool End()
        {
            lock (Sync)
            {
                return Ended = true && Connection.Terminate();
            }
        }
    }

    public abstract class OutgoingMessage<TReq, TRes> : MemoryDuplex, IFinishable, IEndable
        where TReq : Head, new() where TRes : Head, new()
    {
        public TRes Head { get; set; } = new TRes();
        public Connection<TReq, TRes> Connection { get; }

        public Version Version
        {
            get { lock (Sync) return Head.Version; }
            set { lock (Sync) { if (SentHead) return; Head.Version = value; } }
        }
        public string this[string key]
        {
            get { lock (Sync) return Head.Headers[key]; }
            set { lock (Sync) { if (SentHead) return; Head.Headers[key] = value; } }
        }

        public virtual event ControlHandler OnFinish;

        public bool Ended { get; private set; } = false;
        public bool SentHead { get; private set; } = false;
        public bool Finished { get; private set; } = false;

        public OutgoingMessage(Connection<TReq, TRes> connection, Version version)
        {
            Connection = connection;
            Head.Version = version;
        }

        public virtual bool Finish()
        {
            lock (Sync)
            {
                OnFinish?.Invoke();
                return Finished = true;
            }
        }
        public virtual bool SendHead()
        {
            lock (Sync)
            {
                if (SentHead) return false;
                return Connection.StartOutgoing(Head) && (SentHead = true);
            }
        }
        public virtual bool End()
        {
            lock (Sync)
            {
                return (!SentHead ? SendHead() : true) && Connection.FinishOutgoing() && (Ended = true);
            }
        }
    }
}
