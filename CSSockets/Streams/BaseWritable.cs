using System;
using System.IO;
using System.Threading;

namespace CSSockets.Streams
{
    /// <summary>
    /// Encapsulates a UnifiedDuplex to provide a base class for a write-only data pipe.
    /// </summary>
    abstract public class BaseWritable : IBufferedWritable
    {
        protected RawUnifiedDuplex Writable { get; } = new RawUnifiedDuplex();

        public bool Ended => Writable.Ended;
        protected void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null); }

        public long WrittenBytes => Writable.ProcessedBytes;
        public int OutgoingBuffered => Writable.Buffered;
        public bool Corked => Writable.Paused;

        protected BaseWritable() => Writable.OnData += HandleData;

        abstract protected void HandleData(byte[] data);

        virtual public void Write(byte[] data) => Writable.Write(data);
        virtual public void Write(byte[] data, int offset, int count)
        {
            byte[] sliced = new byte[count];
            Buffer.BlockCopy(data, offset, sliced, 0, count);
            Write(sliced);
        }

        virtual public void Unpipe(IReadable from)
        {
            if (from.PipedTo == this) from.Unpipe();
            else throw new InvalidOperationException("The specified readable is not piped to this writable");
        }
        virtual public void Cork() => Writable.Pause();
        virtual public void Uncork()
        {
            Writable.Resume();
            if (Writable.Buffered > 0)
                HandleData(Writable.Read());
        }
        virtual protected void EndWritable() => Writable.End();
        virtual public void End()
        {
            ThrowIfEnded();
            EndWritable();
        }
    }
}
