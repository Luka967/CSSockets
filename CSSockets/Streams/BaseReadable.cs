using System;
using System.IO;
using System.Threading;

namespace CSSockets.Streams
{
    /// <summary>
    /// Encapsulates a UnifiedDuplex to provide a base class for a read-only data pipe.
    /// </summary>
    abstract public class BaseReadable : IBufferedReadable
    {
        protected RawUnifiedDuplex Readable { get; } = new RawUnifiedDuplex();
        public bool Ended => Readable.Ended;
        protected void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null); }

        public long ReadBytes => Readable.ProcessedBytes;
        public int IncomingBuffered => Readable.Buffered;
        public bool Paused => Readable.Paused;
        public IWritable PipedTo => Readable.PipedTo;

        virtual public event DataHandler OnData
        {
            add => Readable.OnData += value;
            remove => Readable.OnData -= value;
        }

        public void Pipe(IWritable to) => Readable.Pipe(to);
        public void Unpipe() => Readable.Unpipe();

        abstract public byte[] Read();
        abstract public byte[] Read(int length);

        virtual public void Pause() => Readable.Pause();
        virtual public void Resume() => Readable.Resume();

        virtual protected void EndReadable() => Readable.End();
        virtual public void End()
        {
            ThrowIfEnded();
            EndReadable();
        }
    }
}
