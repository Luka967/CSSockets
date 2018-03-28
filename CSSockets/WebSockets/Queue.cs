using System.Threading;
using System.Collections.Generic;

namespace CSSockets.WebSockets
{
    internal class Queue<T>
    {
        private readonly List<T> List = new List<T>();
        private readonly object getLock = new object();
        private readonly object putLock = new object();
        private readonly AutoResetEvent getBlock = new AutoResetEvent(false);
        public int Count => List.Count;
        public bool IsEmpty => List.Count == 0;
        public bool Ended { get; private set; } = false;

        public Queue() { }
        public Queue(IEnumerable<T> starting)
        {
            lock (putLock) List.AddRange(starting);
        }

        public bool Enqueue(T item)
        {
            lock (putLock)
            {
                if (Ended) return false;
                List.Add(item);
                getBlock.Set();
                return true;
            }
        }

        public bool Dequeue(out T item)
        {
            lock (getLock)
            {
                item = default(T);
                if (Ended) return false;
                bool got = false;
                getBlock.WaitOne();
                if (!Ended)
                {
                    item = List[0];
                    List.RemoveAt(0);
                    if (List.Count == 0)
                        getBlock.Reset();
                    got = true;
                }
                return got;
            }
        }

        public bool End()
        {
            lock (getLock)
            {
                if (Ended) return false;
                Ended = true;
                getBlock.Set();
                getBlock.Dispose();
                return true;
            }
        }
    }
}
