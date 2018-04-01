using System;
using System.Threading;

namespace CSSockets.Streams
{
    /// <summary>
    /// Represents a duplex stream that shares a single data buffer for read/write operations.
    /// </summary>
    public abstract class UnifiedDuplex : IDuplex, IPausable, IDrainable
    {
        public bool Ended { get; private set; } = false;
        protected void ThrowIfEnded()
        {
            if (Ended) throw new InvalidOperationException("This stream has ended.");
        }

        protected readonly PrimitiveBuffer buffer = new PrimitiveBuffer();

        protected readonly object Rlock = new object();
        protected readonly object Wlock = new object();
        protected ulong? Rtarget { get; private set; } = null;
        protected readonly AutoResetEvent Rwait = new AutoResetEvent(false);
        protected readonly ManualResetEvent Rpause = new ManualResetEvent(true);

        private bool Rpaused = false;
        private IWritable Rpipe = null;
        private ulong Wwritten = 0;

        public ulong Buffered { get { ThrowIfEnded(); return buffer.Length; } }
        public ulong ReadCount { get { ThrowIfEnded(); return WriteCount - Buffered; } }
        public ulong WriteCount { get { ThrowIfEnded(); return Wwritten; } }

        public bool IsPaused { get { ThrowIfEnded(); return Rpaused; } }
        public IWritable PipedTo { get { ThrowIfEnded(); return Rpipe; } }

        private event DataHandler _dataEvent;
        public virtual event DataHandler OnData
        {
            add
            {
                lock (Rlock) lock (Wlock)
                    {
                        ThrowIfEnded();
                        _dataEvent += value;
                        BcheckNewPathing();
                    }
            }
            remove
            {
                lock (Rlock) lock (Wlock)
                    {
                        ThrowIfEnded();
                        _dataEvent -= value;
                    }
            }
        }
        public virtual event ControlHandler OnEnd;
        public virtual event ControlHandler OnFail;
        public virtual event ControlHandler OnDrain;

        public virtual bool Pause()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    if (IsPaused) return false;
                    Rpaused = true;
                    Rpause.Reset();
                    return true;
                }
        }
        public virtual bool Resume()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    if (!IsPaused) return false;
                    Rpaused = false;
                    Rpause.Set();
                    BcheckNewPathing();
                    return true;
                }
        }

        public virtual bool Pipe(IWritable to)
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    if (to == null) return false;
                    Rpipe = to;
                    BcheckNewPathing();
                    return true;
                }
        }
        public virtual bool Unpipe()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    Rpipe = null;
                    return true;
                }
        }
        public virtual bool Unpipe(IReadable from)
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    if (from == null) return false;
                    if (from.PipedTo != this) return false;
                    from.Unpipe();
                    return true;
                }
        }

        protected byte[] Bread()
        {
            lock (Rlock)
            {
                ThrowIfEnded();
                Rpause.WaitOne();
                if (Ended) return null;
                bool waiting = false;
                lock (Wlock) if (Buffered == 0) waiting = true;
                if (waiting)
                {
                    Rtarget = 1;
                    Rwait.WaitOne();
                    if (Ended) return null;
                    Rwait.Reset();
                }
                ulong length;
                lock (Wlock) length = Buffered;
                byte[] dst = new byte[length];
                Read(dst);
                return dst;
            }
        }
        protected byte[] Bread(ulong length)
        {
            lock (Rlock)
            {
                ThrowIfEnded();
                Rpause.WaitOne();
                if (Ended) return null;
                bool waiting = false;
                lock (Wlock) if (Buffered < length) waiting = true;
                if (waiting)
                {
                    Rtarget = length;
                    Rwait.WaitOne();
                    if (Ended) return null;
                    Rwait.Reset();
                }
                byte[] dst = new byte[length];
                Read(dst);
                if (Buffered == 0) OnDrain?.Invoke();
                return dst;
            }
        }
        protected ulong Bread(byte[] destination)
        {
            lock (Rlock) lock (Wlock)
                {
                    ThrowIfEnded();
                    ulong length = Math.Min(Buffered, (ulong)destination.LongLength);
                    if (Buffered > 0) buffer.Read(destination, length);
                    return length;
                }
        }

        protected bool Bwrite(byte[] source)
        {
            lock (Wlock)
            {
                if (Ended) return false;
                buffer.Write(source);
                if (Rtarget == null || Rtarget < Buffered) return true;
                Rwait.Set();
                return true;
            }
        }
        protected bool Bhandle(byte[] data)
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (data.Length == 0) return false;
                if (Rpaused) return Bwrite(data);
                ulong len = (ulong)data.LongLength;
                Wwritten += len;
                bool something = false;
                if (Rpipe != null)
                {
                    if (Rpipe.Ended || !Rpipe.Write(data)) OnFail?.Invoke();
                    something = true;
                }
                if (_dataEvent != null) { _dataEvent?.Invoke(data); something = true; }
                if (Rtarget != null || !something) Bwrite(data);
                return true;
            }
        }
        protected void BcheckNewPathing()
        {
            if (IsPaused) return;
            if (Buffered == 0) return;
            Bhandle(Read());
        }

        protected void FireFail() => OnFail?.Invoke();
        public abstract bool Write(byte[] source);
        public abstract bool Write(byte[] source, ulong start, ulong end);
        public abstract byte[] Read();
        public abstract byte[] Read(ulong length);
        public abstract ulong Read(byte[] destination);

        public virtual bool End()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Ended) return false;
                    Ended = true;
                    Rpause.Set();
                    Rpause.Dispose();
                    Rwait.Set();
                    Rwait.Dispose();
                    OnEnd?.Invoke();
                    return true;
                }
        }
    }
}
