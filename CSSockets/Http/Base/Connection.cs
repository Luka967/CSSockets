using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Reference;

namespace CSSockets.Http.Base
{
    public abstract class Connection<TParse, TSerialize> : IEndable
        where TParse : Head, new()
        where TSerialize : Head, new()
    {
        public Connection Base { get; } = null;
        public bool Ended { get; private set; } = false;
        public bool Terminated { get; private set; } = false;
        protected readonly object OpsLock = new object();

        public event ControlHandler OnEnd;

        public HeadParser<TParse> HeadParser { get; protected set; } = null;
        public HeadSerializer<TSerialize> HeadSerializer { get; protected set; } = null;
        public BodyParser BodyParser { get; protected set; } = null;
        public BodySerializer BodySerializer { get; protected set; } = null;

        public Connection(Connection connection)
        {
            Base = connection;
            Base.OnClose += () => End();
            Initialize();
        }
        protected abstract bool Initialize();
        public abstract bool SendHead(TSerialize head);
        public abstract bool FinishResponse();
        public abstract bool Freeze();
        public abstract bool Abandon();

        public virtual bool Terminate()
        {
            lock (OpsLock)
            {
                if (Ended || Terminated) return false;
                Terminated = true;
                Base.Terminate();
                return End();
            }
        }
        public virtual bool End()
        {
            lock (OpsLock)
            {
                if (Ended) return false;
                Ended = true;
                if (Base.State == TcpSocketState.Open && !Terminated) Base.End();
                OnEnd?.Invoke();
                return Abandon();
            }
        }
    }
}
