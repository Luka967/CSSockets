namespace CSSockets.Streams
{
    public class BaseReadable<TReadable> : IBufferedReadable
        where TReadable : UnifiedDuplex, new()
    {
        protected readonly TReadable Readable = new TReadable();

        public bool Ended => Readable.Ended;
        public bool IsPaused => Readable.IsPaused;
        public ulong ReadCount => Readable.WriteCount;
        public IWritable PipedTo => Readable.PipedTo;
        public ulong BufferedReadable => Readable.Buffered;

        public virtual event DataHandler OnData
        {
            add => Readable.OnData += value;
            remove => Readable.OnData -= value;
        }
        public virtual event ControlHandler OnFail
        {
            add => Readable.OnFail += value;
            remove => Readable.OnFail -= value;
        }
        public virtual event ControlHandler OnEnd
        {
            add => Readable.OnEnd += value;
            remove => Readable.OnEnd -= value;
        }

        public virtual bool Pause() => Readable.Pause();
        public virtual bool Resume() => Readable.Resume();
        public virtual bool Pipe(IWritable to) => Readable.Pipe(to);
        public virtual bool Unpipe() => Readable.Unpipe();
        public virtual byte[] Read() => Readable.Read();
        public virtual byte[] Read(ulong length) => Readable.Read(length);
        public virtual ulong Read(byte[] destination) => Readable.Read(destination);
        public virtual bool End() => Readable.End();
    }
    public abstract class BaseReadable : BaseReadable<MemoryDuplex> { }

    public abstract class BaseWritable<TWritable> : IBufferedWritable
        where TWritable : UnifiedDuplex, new()
    {
        protected readonly TWritable Writable = new TWritable();
        protected readonly object Wlock = new object();

        public BaseWritable() => Writable.OnData += HandleData;

        public bool Ended => Writable.Ended;
        public bool IsCorked => Writable.IsPaused;
        public ulong WriteCount => Writable.WriteCount;
        public ulong BufferedWritable => Writable.Buffered;

        public virtual event ControlHandler OnEnd
        {
            add => Writable.OnEnd += value;
            remove => Writable.OnEnd -= value;
        }
        public virtual event ControlHandler OnDrain
        {
            add => Writable.OnDrain += value;
            remove => Writable.OnDrain -= value;
        }

        protected abstract void HandleData(byte[] data);

        public virtual bool Cork() => Writable.Pause();
        public virtual bool Uncork() => Writable.Resume();
        public virtual bool End() => Writable.End();
        public virtual bool Unpipe(IReadable from) => Writable.Unpipe(from);
        public virtual bool Write(byte[] source) => Writable.Write(source);
        public virtual bool Write(byte[] source, ulong start, ulong end) => Writable.Write(source, start, end);
    }
    public abstract class BaseWritable : BaseWritable<MemoryDuplex> { }

    public abstract class BaseDuplex<TReadable, TWritable> : IBufferedDuplex
        where TReadable : UnifiedDuplex, new()
        where TWritable : UnifiedDuplex, new()
    {
        protected readonly TReadable Readable = new TReadable();
        protected readonly TWritable Writable = new TWritable();
        protected readonly object EndLock = new object();

        public bool ReadableEnded => Readable.Ended;
        public bool WritableEnded => Writable.Ended;
        public bool Ended => Readable.Ended && Writable.Ended;
        public bool IsPaused => Readable.IsPaused;
        public bool IsCorked => Writable.IsPaused;
        public IWritable PipedTo => Readable.PipedTo;
        public ulong ReadCount => Readable.WriteCount;
        public ulong WriteCount => Writable.WriteCount;
        public ulong BufferedReadable => Readable.Buffered;
        public ulong BufferedWritable => Writable.Buffered;

        public virtual event ControlHandler OnEnd;
        public virtual event DataHandler OnData
        {
            add => Readable.OnData += value;
            remove => Readable.OnData -= value;
        }
        public virtual event ControlHandler OnFail
        {
            add => Readable.OnFail += value;
            remove => Readable.OnFail -= value;
        }
        public virtual event ControlHandler OnDrain
        {
            add => Writable.OnDrain += value;
            remove => Writable.OnDrain -= value;
        }

        public virtual bool Pause() => Readable.Pause();
        public virtual bool Resume() => Readable.Resume();
        public virtual bool Pipe(IWritable to) => Readable.Pipe(to);
        public virtual bool Unpipe() => Readable.Unpipe();
        public virtual bool Cork() => Writable.Pause();
        public virtual bool Uncork() => Writable.Resume();
        public virtual bool Unpipe(IReadable from) => Writable.Unpipe(from);
        public abstract byte[] Read();
        public abstract byte[] Read(ulong length);
        public abstract ulong Read(byte[] destination);
        public abstract bool Write(byte[] source);
        public abstract bool Write(byte[] source, ulong start, ulong end);

        protected bool EndHasListeners => OnEnd != null;
        protected void FireEnd() => OnEnd?.Invoke();
        protected virtual bool EndReadable()
        {
            lock (EndLock)
            {
                if (Readable.Ended) return false;
                Readable.End();
                if (Writable.Ended) OnEnd?.Invoke();
                return true;
            }
        }
        protected virtual bool EndWritable()
        {
            lock (EndLock)
            {
                if (Writable.Ended) return false;
                Writable.End();
                if (Readable.Ended) OnEnd?.Invoke();
                return true;
            }
        }
        public virtual bool End()
        {
            lock (EndLock) return EndReadable() | EndWritable();
        }
    }
    public abstract class BaseDuplex : BaseDuplex<MemoryDuplex, MemoryDuplex> { }
}
