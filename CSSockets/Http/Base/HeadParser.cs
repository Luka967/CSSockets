using CSSockets.Base;
using CSSockets.Streams;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Base
{
    abstract public class HeadParser<T> : UnifiedDuplex, IQueueableAsyncOutputter<T>
        where T : MessageHead, new()
    {
        public event AsyncCreationHandler<T> OnOutput;
        protected Queue<T> HeadQueue { get; } = new Queue<T>();
        public int QueuedCount => HeadQueue.Count;
        protected void PushIncoming()
        {
            if (OnOutput != null) OnOutput(Incoming);
            else HeadQueue.Enqueue(Incoming);
            Incoming = new T();
        }
        public T Next()
        {
            ThrowIfEnded();
            if (!HeadQueue.Dequeue(out T item))
                // ended
                return null;
            return item;
        }
        protected T Incoming { get; set; } = new T();
        protected StringQueue StringQueue { get; } = new StringQueue();

        abstract protected int ProcessData(byte[] data, bool writeExcess);

        protected const char WHITESPACE = ' ';
        protected const char COLON = ':';
        protected const char CR = '\r';
        protected const char LF = '\n';

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data) => ProcessData(data, true);
        public int WriteSafe(byte[] data) => ProcessData(data, false);

        public override void End()
        {
            base.End();
            HeadQueue.End();
        }
    }
}
