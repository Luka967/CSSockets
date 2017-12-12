using System;
using System.Threading;
using CSSockets.Streams;
using System.Collections.Generic;

namespace CSSockets.Base
{
    public class Queue<T> : IEndable
    {
        private List<T> List { get; }
        private object GetterLock { get; }
        private EventWaitHandle GetterBlock { get; }
        public int Count => List.Count;
        public bool IsEmpty => List.Count == 0;
        public bool Ended { get; private set; }
        private void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This queue has already ended.", innerException: null); }

        public Queue()
        {
            List = new List<T>();
            GetterLock = new object();
            GetterBlock = new EventWaitHandle(false, EventResetMode.ManualReset);
        }
        public Queue(IEnumerable<T> starting) : this() => List.AddRange(starting);

        public void Enqueue(T item)
        {
            ThrowIfEnded();
            List.Add(item);
            GetterBlock.Set();
        }

        public bool Dequeue(out T item)
        {
            ThrowIfEnded();
            bool got = false;
            item = default(T);
            lock (GetterLock)
            {
                GetterBlock.WaitOne();
                if (!Ended)
                {
                    item = List[0];
                    List.RemoveAt(0);
                    if (List.Count == 0)
                        GetterBlock.Reset();
                    got = true;
                }
            }
            return got;
        }

        public void End()
        {
            ThrowIfEnded();
            lock (GetterLock)
            {
                Ended = true;
                GetterBlock.Set();
                GetterBlock.Dispose();
            }
        }
    }
}
