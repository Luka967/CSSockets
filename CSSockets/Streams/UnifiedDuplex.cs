using System;
using System.IO;
using System.Threading;

namespace CSSockets.Streams
{
    /// <summary>
    /// Provides a basic, two-way memory-driven data pipe. This class can't be inherited.
    /// </summary>
    abstract public class UnifiedDuplex : IUnifiedDuplex
    {
        protected MemoryStream Bstream { get; } = new MemoryStream();
        protected int BreadIndex { get; set; } = 0;
        protected int BwaitIndex { get; set; } = -1;
        protected int BwriteIndex { get; set; } = 0;
        protected object BreadLock { get; } = new object();
        protected object BwriteLock { get; } = new object();
        protected EventWaitHandle Bblock { get; } = new EventWaitHandle(true, EventResetMode.ManualReset);
        protected EventWaitHandle Bwait { get; } = new EventWaitHandle(false, EventResetMode.ManualReset);

        public long ProcessedBytes { get; protected set; } = 0;
        protected bool ThrowIfEnded()
        {
            if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null);
            return false;
        }
        public void ThrowIfPipedOrAsync()
        {
            if (PipedTo != null) throw new InvalidOperationException("The operation will never succeed because the stream is piped");
            else if (_OnData != null) throw new InvalidOperationException("The operation will never because an OnData event has been set");
        }

        public int Buffered => !ThrowIfEnded() ? BwriteIndex - BreadIndex : -1;
        public IWritable PipedTo { get; private set; } = null;
        public bool Ended { get; private set; } = false;
        public bool Paused { get; private set; } = false;

        private event DataHandler _OnData;
        virtual public event DataHandler OnData
        {
            add
            {
                ThrowIfEnded();
                _OnData += value;
                BtestNewPathing();
            }
            remove
            {
                ThrowIfEnded();
                _OnData -= value;
            }
        }

        protected void Bhandle(byte[] data)
        {
            ThrowIfEnded();
            if (data == null) return;
            ProcessedBytes += data.LongLength;
            if (Paused) Bwrite(data);
            else if (PipedTo != null)
                PipedTo.Write(data);
            else if (_OnData != null)
                _OnData(data);
            else Bwrite(data);
        }

        protected void BtestNewPathing()
        {
            if (Paused || Buffered == 0) return;
            Bhandle(Bread());
        }

        protected void Bwrite(byte[] data)
        {
            ThrowIfEnded();
            lock (BwriteLock)
            {
                Bstream.Position = BwriteIndex;
                Bstream.Write(data, 0, data.Length);
                BwriteIndex += data.Length;
                if (BreadIndex != -1 && BwriteIndex >= BreadIndex)
                    Bwait.Set();
            }
        }
        protected byte[] Bread()
        {
            ThrowIfEnded();
            byte[] data;
            if (Buffered == 0)
            {
                BwaitIndex = BwriteIndex + 1;
                Bwait.WaitOne();
                if (Ended) return null;
                Bwait.Reset();
                BwaitIndex = -1;
            }
            lock (BreadLock)
            {
                int length = Buffered;
                data = new byte[length];
                lock (BwriteLock)
                {
                    Bstream.Position = BreadIndex;
                    Bstream.Read(data, 0, length);
                    BreadIndex += length;
                }
            }
            Btrim();
            return data;
        }
        protected byte[] Bread(int length)
        {
            ThrowIfEnded();
            byte[] data;
            if (Buffered < length)
            {
                BwaitIndex = BreadIndex + length;
                Bwait.WaitOne();
                if (Ended) return null;
                Bwait.Reset();
                BwaitIndex = -1;
            }
            lock (BreadLock)
            {
                data = new byte[length];
                lock (BwriteLock)
                {
                    Bstream.Position = BreadIndex;
                    Bstream.Read(data, 0, length);
                    BreadIndex += length;
                }
            }
            Btrim();
            return data;
        }
        protected void Btrim()
        {
            ThrowIfEnded();
            lock (BreadLock)
            {
                lock (BwriteLock)
                {
                    int buffered = Buffered;
                    Bstream.Position = BreadIndex;
                    byte[] remain = new byte[buffered];
                    Bstream.Read(remain, 0, buffered);
                    Bstream.Position = 0;
                    Bstream.Write(remain, 0, buffered);
                    BreadIndex = 0;
                    BwriteIndex = buffered;
                }
            }
        }

        virtual public void End()
        {
            ThrowIfEnded();
            lock (BreadLock)
            {
                lock (BwriteLock)
                {
                    Ended = true;
                    Bstream.Dispose();
                    Bblock.Set();
                    Bwait.Set();
                    Bblock.Dispose();
                    Bwait.Dispose();
                    PipedTo = null;
                    Paused = false;
                }
            }
        }
        virtual public void Pause()
        {
            ThrowIfEnded();
            lock (BreadLock)
            {
                Paused = true;
                Bblock.Reset();
            }
        }
        virtual public void Pipe(IWritable to)
        {
            lock (BreadLock)
                lock (BwriteLock)
                {
                    ThrowIfEnded();
                    PipedTo = to;
                    BtestNewPathing();
                }
        }
        virtual public void Unpipe(IReadable from)
        {
            lock (BreadLock) lock (BwriteLock)
                {
                    ThrowIfEnded();
                    if (from.PipedTo == this) from.Unpipe();
                    else throw new InvalidOperationException("The specified readable is not piped to this writable");
                }
        }
        abstract public byte[] Read();
        abstract public byte[] Read(int length);
        virtual public void Resume()
        {
            ThrowIfEnded();
            lock (BreadLock)
            {
                Paused = false;
                Bblock.Set();
                BtestNewPathing();
            }
        }
        public void Unpipe()
        {
            lock (BreadLock)
            {
                ThrowIfEnded();
                PipedTo = null;
            }
        }

        abstract public void Write(byte[] data);
        virtual public void Write(byte[] data, int offset, int count)
        {
            byte[] sliced = new byte[count];
            Buffer.BlockCopy(data, offset, sliced, 0, count);
            Write(sliced);
        }
    }
}
