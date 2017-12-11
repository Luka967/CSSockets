using System;
using System.IO;
using System.Threading;

namespace WebSockets.Streams
{
    /// <summary>
    /// Encapsulates two UnifiedDuplex instances to provide a base class for a read-write data pipe.
    /// </summary>
    abstract public class BaseDuplex : IBufferedDuplex
    {
        protected RawUnifiedDuplex Readable { get; } = new RawUnifiedDuplex();
        protected RawUnifiedDuplex Writable { get; } = new RawUnifiedDuplex();
        protected object EndLock { get; } = new object();

        public bool Ended => Readable.Ended && Writable.Ended;
        public bool ReadableEnded => Readable.Ended;
        public bool WritableEnded => Writable.Ended;
        protected void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null); }
        protected void ThrowIfReadableEnded()
        {
            if (Readable.Ended) throw new ObjectDisposedException("The readable part of this duplex has already ended.", innerException: null);
        }
        protected void ThrowIfWritableEnded()
        {
            if (Writable.Ended) throw new ObjectDisposedException("The writable part of this duplex has already ended.", innerException: null);
        }

        public long ReadBytes => Readable.ProcessedBytes;
        public int IncomingBuffered => Readable.Buffered;
        public bool Paused => Readable.Paused;
        public IWritable PipedTo => Readable.PipedTo;

        public long WrittenBytes => Writable.ProcessedBytes;
        public int OutgoingBuffered => Writable.Buffered;
        public bool Corked => Writable.Paused;

        virtual public event DataHandler OnData
        {
            add => Readable.OnData += value;
            remove => Readable.OnData -= value;
        }

        public void Pipe(IWritable to) => Readable.Pipe(to);
        public void Unpipe() => Readable.Unpipe();
        virtual public void Unpipe(IReadable from)
        {
            if (from.PipedTo == this) from.Unpipe();
            else throw new InvalidOperationException("This readable is not piped to this writable");
        }

        abstract public byte[] Read();
        abstract public byte[] Read(int length);

        virtual public void Pause() => Readable.Pause();
        virtual public void Resume() => Readable.Resume();

        abstract public void Write(byte[] data);
        virtual public void Write(byte[] data, int offset, int count)
        {
            byte[] sliced = new byte[count];
            Buffer.BlockCopy(data, offset, sliced, 0, count);
            Write(sliced);
        }

        virtual public void Cork() => Writable.Pause();
        virtual public void Uncork() => Writable.Resume();

        virtual protected void OnEnded() { }
        virtual protected void EndReadable()
        {
            lock (EndLock)
            {
                Readable.End();
                if (WritableEnded) OnEnded();
            }
        }
        virtual protected void EndWritable()
        {
            lock (EndLock)
            {
                Writable.End();
                if (ReadableEnded) OnEnded();
            }
        }
        virtual public void End()
        {
            ThrowIfEnded();
            EndReadable();
            EndWritable();
        }
    }
}
