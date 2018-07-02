using System;
using System.IO;

namespace CSSockets.Streams
{
    public sealed class SimpleStream : Stream
    {
        private readonly PrimitiveBuffer Buffer = new PrimitiveBuffer();
        private readonly object Sync = new object();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => (long)Buffer.Length;
        public override long Position { get => 0; set => throw new InvalidOperationException("Cannot seek"); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (Sync)
            {
                int reading = Math.Min((int)Length, count);
                Buffer.Read(buffer, (ulong)reading, (uint)offset);
                return reading;
            }
        }
        public override long Seek(long offset, SeekOrigin origin)
            => throw new InvalidOperationException("Cannot seek");
        public override void SetLength(long value)
            => throw new InvalidOperationException("Cannot set length");
        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (Sync) Buffer.Write(PrimitiveBuffer.Slice(buffer, offset, offset + count));
        }
    }
    public abstract class Readable : IReadable
    {
        protected readonly PrimitiveBuffer Buffer = new PrimitiveBuffer();
        protected readonly object Sync = new object();

        public IWritable PipedTo { get; private set; } = null;
        public ulong ReadCount { get; private set; } = 0;
        public ulong BufferedReadable => Buffer.Length;

        protected event DataHandler _OnData;
        public virtual event DataHandler OnData
        {
            add
            {
                lock (Sync)
                {
                    _OnData += value;
                    if (BufferedReadable > 0) HandleReadable(Read());
                }
            }
            remove
            {
                lock (Sync) _OnData -= value;
            }
        }
        public virtual event ControlHandler OnFail;

        public virtual bool Pipe(IWritable to)
        {
            lock (Sync)
            {
                if (to == null) return false;
                PipedTo = to;
                if (BufferedReadable > 0) HandleReadable(Read());
                return true;
            }
        }
        public virtual bool Burst(IWritable to)
        {
            lock (Sync)
            {
                if (to == null) return false;
                if (BufferedReadable > 0) return to.Write(Read());
                return true;
            }
        }
        public virtual bool Unpipe()
        {
            lock (Sync)
            {
                PipedTo = null;
                return true;
            }
        }

        protected bool HandleReadable(byte[] source)
        {
            lock (Sync)
            {
                if (PipedTo != null)
                {
                    if (!PipedTo.Write(source)) OnFail?.Invoke();
                    return true;
                }
                if (_OnData != null) { _OnData(source); return true; }
                return Buffer.Write(source);
            }
        }
        public virtual ulong Read(byte[] destination, ulong start = 0)
        {
            lock (Sync)
            {
                ulong length = BufferedReadable;
                byte[] data = Buffer.Read(length);
                PrimitiveBuffer.Copy(data, 0, destination, start, length);
                ReadCount += length;
                return length;
            }
        }
        public virtual byte[] Read(ulong length)
        {
            lock (Sync)
            {
                ulong retLength = Math.Min(BufferedReadable, length);
                if (retLength != length) return null;
                ReadCount += retLength;
                return Buffer.Read(length);
            }
        }
        public virtual byte[] Read()
        {
            lock (Sync)
            {
                ReadCount += BufferedReadable;
                return Buffer.Read(BufferedReadable);
            }
        }
    }

    public abstract class Writable : IWritable
    {
        protected readonly object Sync = new object();

        public ulong WriteCount { get; private set; } = 0;

        public virtual bool Unpipe(IReadable from)
        {
            lock (Sync) return from.PipedTo == this ? from.Unpipe() : false;
        }

        protected abstract bool HandleWritable(byte[] source);
        public virtual bool Write(byte[] source)
        {
            lock (Sync) return HandleWritable(source);
        }
        public virtual bool Write(byte[] source, ulong start)
            => Write(PrimitiveBuffer.Slice(source, start, (ulong)source.LongLength));
        public virtual bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));
    }

    public abstract class Duplex : IDuplex
    {
        protected readonly PrimitiveBuffer Readable = new PrimitiveBuffer();
        protected readonly object Sync = new object();

        public IWritable PipedTo { get; private set; } = null;
        public ulong ReadCount { get; private set; } = 0;
        public ulong WriteCount { get; private set; } = 0;
        public ulong BufferedReadable => Readable.Length;

        protected event DataHandler _OnData;
        public virtual event DataHandler OnData
        {
            add
            {
                lock (Sync)
                {
                    _OnData += value;
                    if (BufferedReadable > 0) HandleReadable(Read());
                }
            }
            remove
            {
                lock (Sync) _OnData -= value;
            }
        }
        public virtual event ControlHandler OnFail;

        public virtual bool Pipe(IWritable to)
        {
            lock (Sync)
            {
                if (to == null) return false;
                PipedTo = to;
                if (BufferedReadable > 0) HandleReadable(Read());
                return true;
            }
        }
        public virtual bool Burst(IWritable to)
        {
            lock (Sync)
            {
                if (to == null) return false;
                if (BufferedReadable > 0) return to.Write(Read());
                return true;
            }
        }
        public virtual bool Unpipe()
        {
            lock (Sync)
            {
                PipedTo = null;
                return true;
            }
        }
        public virtual bool Unpipe(IReadable from)
        {
            lock (Sync) return from.PipedTo == this ? from.Unpipe() : false;
        }

        public virtual ulong Read(byte[] destination, ulong start = 0)
        {
            lock (Sync)
            {
                ulong length = Math.Min(BufferedReadable, (ulong)destination.LongLength);
                byte[] data = Readable.Read(length);
                PrimitiveBuffer.Copy(data, 0, destination, start, length);
                ReadCount += length;
                return length;
            }
        }
        public virtual byte[] Read(ulong length)
        {
            lock (Sync)
            {
                ulong retLength = Math.Min(BufferedReadable, length);
                if (retLength != length) return null;
                ReadCount += retLength;
                return Readable.Read(length);
            }
        }
        public virtual byte[] Read()
        {
            lock (Sync)
            {
                ReadCount += BufferedReadable;
                return Readable.Read(BufferedReadable);
            }
        }

        protected bool HandleReadable(byte[] source)
        {
            lock (Sync)
            {
                if (PipedTo != null)
                {
                    if (!PipedTo.Write(source)) OnFail?.Invoke();
                    return true;
                }
                if (_OnData != null) { _OnData(source); return true; }
                return Readable.Write(source);
            }
        }
        protected abstract bool HandleWritable(byte[] source);
        public virtual bool Write(byte[] source)
        {
            lock (Sync) return HandleWritable(source);
        }
        public virtual bool Write(byte[] source, ulong start)
            => Write(PrimitiveBuffer.Slice(source, start, (ulong)source.LongLength));
        public virtual bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));
    }

    public abstract class Transform<T> : Readable, ITransform<T>
    {
        public abstract bool Write(T source);
    }
    public abstract class Collector<T> : Writable, ICollector<T>
    {
        public event OutputHandler<T> OnCollect;

        protected bool Pickup(T item)
        {
            if (OnCollect != null) { OnCollect(item); return true; }
            return false;
        }
    }
    public abstract class Translator<T> : Duplex, ICollector<T>
    {
        public event OutputHandler<T> OnCollect;

        protected bool Pickup(T item)
        {
            if (OnCollect != null) { OnCollect(item); return true; }
            return false;
        }
    }

    public class MemoryDuplex : Duplex
    {
        protected override bool HandleWritable(byte[] source) => HandleReadable(source);
    }
    public class VoidWritable : Writable
    {
        public static readonly VoidWritable Default = new VoidWritable();
        protected override bool HandleWritable(byte[] source) => true;
    }
}
