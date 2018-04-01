using System;
using System.Text;
using System.Collections.Concurrent;

namespace CSSockets.Http.Structures
{
    public sealed class StringQueue
    {
        private ConcurrentQueue<StringBuilder> Queue { get; } = new ConcurrentQueue<StringBuilder>();
        private StringBuilder Last { get; set; } = null;

        public StringQueue() => New();

        public void New()
        {
            StringBuilder item = new StringBuilder();
            Last = item;
            Queue.Enqueue(item);
        }
        public void Append(char c)
        {
            if (Last == null) throw new InvalidOperationException("Nothing is in queue");
            Last.Append(c);
        }
        public string Next()
        {
            if (Queue.IsEmpty) throw new InvalidOperationException("Queue is empty");
            if (!Queue.TryDequeue(out StringBuilder item))
                throw new Exception("Couldn't dequeue");
            if (item == Last) Last = null;
            return item.ToString();
        }
    }
}
