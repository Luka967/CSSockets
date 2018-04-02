using System.Text;
using System.Threading;
using CSSockets.Streams;
using CSSockets.Http.Structures;
using System.Collections.Concurrent;

namespace CSSockets.Http.Base
{
    public class Head
    {
        public Version Version { get; set; } = null;
        public HeaderCollection Headers { get; set; } = new HeaderCollection();
    }

    public abstract class HeadParser<THead> : UnifiedDuplex, IQueueingOutputter<THead>
        where THead : Head, new()
    {
        protected const char WHITESPACE = ' ';
        protected const char COLON = ':';
        protected const char CR = '\r';
        protected const char LF = '\n';

        protected THead Current = new THead();
        protected StringQueue CsQueue = new StringQueue();
        protected bool Malformed = false;

        private readonly ConcurrentQueue<THead> Qbuf = new ConcurrentQueue<THead>();
        private readonly AutoResetEvent Qwait = new AutoResetEvent(false);
        private readonly object Qlock = new object();
        private bool Qwaiting = false;

        public event OutputterHandler<THead> _OnOutput;
        public event OutputterHandler<THead> OnOutput
        {
            add
            {
                _OnOutput += value;
                while (Qbuf.TryDequeue(out THead head))
                    _OnOutput(head);
            }
            remove => _OnOutput -= value;
        }
        public int Queued => Qbuf.Count;

        public THead Next()
        {
            lock (Qlock)
            {
                ThrowIfEnded();
                if (Qbuf.Count == 0)
                {
                    Qwaiting = true;
                    Qwait.WaitOne();
                    if (Ended) return null;
                    Qwait.Reset();
                    Qwaiting = false;
                }
                if (Qbuf.TryDequeue(out THead ret)) return ret;
                throw new System.Exception("Should never happen");
            }
        }
        protected virtual void Push()
        {
            if (_OnOutput == null) Qbuf.Enqueue(Current);
            else
            {
                _OnOutput(Current);
                if (Qwaiting) Qbuf.Enqueue(Current);
            }
            Current = new THead();
        }

        public override byte[] Read() => Bread();
        public override byte[] Read(ulong length) => Bread(length);
        public override ulong Read(byte[] destination) => Bread(destination);

        protected abstract bool TryContinue(byte[] data);

        public override bool Write(byte[] source)
        {
            lock (Wlock) return Ended ? false : TryContinue(source);
        }
        public override bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));

        public override bool End()
        {
            if (base.End())
            {
                Qwait.Set();
                Qwait.Dispose();
                if (Malformed) FireFail();
                return true;
            }
            return false;
        }
    }

    public abstract class HeadSerializer<THead> : BaseReadable
        where THead : Head, new()
    {
        protected const char WHITESPACE = ' ';
        protected const char COLON = ':';
        protected const char CR = '\r';
        protected const char LF = '\n';
        protected readonly object Wlock = new object();

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(ulong length) => Readable.Read(length);
        public override ulong Read(byte[] destination) => Readable.Read(destination);

        protected abstract string Stringify(THead head);
        public bool Write(THead head)
        {
            lock (Wlock) return !Ended && Readable.Write(Encoding.ASCII.GetBytes(Stringify(head)));
        }
    }
}