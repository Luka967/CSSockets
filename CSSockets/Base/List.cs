using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

namespace CSSockets.Base
{
    // Scrapped
#if false
    public class List<T> : IEnumerable<T>
    {
        private delegate void Iterator(T item, ref bool stopIterating, ref ulong index);

        public const ulong MIN_CAPACITY = 4;

        private T[] array { get; set; }
        public ulong Count { get; private set; }
        public ulong Capacity { get; private set; }
        public bool Expandable { get; set; }
        public object Level1Lock { get; private set; } // getters, setters
        public object Level2Lock { get; private set; } // range operations
        public object Level3Lock { get; private set; } // public accessors
        public object Level4Lock { get; private set; } // public iterators

        private void Initialize(ulong len = MIN_CAPACITY, bool expandable = true)
        {
            array = new T[len];
            Capacity = len; Count = 0;
            Expandable = expandable;
            Level1Lock = new object();
            Level2Lock = new object();
            Level3Lock = new object();
            Level4Lock = new object();
        }

        private void RangeCopy(T[] other, ulong srcBegin, ulong dstBegin, ulong count)
        {
            lock (Level1Lock)
            {
                CheckIndex(srcBegin);
                CheckIndex(srcBegin + count, true);
                if (dstBegin > srcBegin)
                    for (ulong i = 0; i < count; i++)
                        other[dstBegin + count - i] = array[srcBegin + count - i];
                else for (ulong i = 0; i < count; i++)
                        other[dstBegin + i] = array[srcBegin + i];
            }
        }
        private T Get(ulong index)
        {
            T item;
            lock (Level1Lock)
            {
                CheckIndex(index);
                item = array[index];
            }
            return item;
        }
        private void Set(ulong index, T item)
        {
            lock (Level1Lock)
            {
                CheckIndex(index, true);
                array[index] = item;
            }
        }
        public void CheckIndex(ulong index, bool inclLast = false)
        {
            if ((inclLast && index > Count) || (!inclLast && index >= Count))
                throw new ArgumentOutOfRangeException("index");
        }

        private void ShrinkCapacity(ulong newCount)
        {
            lock (Level2Lock)
            {
                Count = newCount;
                ulong newCapacity = Capacity;
                while (newCapacity / 2 > newCount && newCapacity > MIN_CAPACITY)
                    newCapacity /= 2;
                if (newCapacity == Capacity) return;
                T[] newArray = new T[newCapacity];
                RangeCopy(newArray, 0, 0, newCount);
                array = newArray;
                Capacity = newCapacity;
            }
        }
        private void EnsureCapacity(ulong newCount)
        {
            lock (Level2Lock)
            {
                ulong oldCount = Count;
                Count = newCount;
                ulong newCapacity = Capacity;
                while (newCapacity < newCount)
                    newCapacity *= 2;
                if (newCapacity == Capacity) return;
                T[] newArray = new T[newCapacity];
                RangeCopy(newArray, 0, 0, oldCount);
                array = newArray;
                Capacity = newCapacity;
            }
        }
        private void RunDelegate(Iterator @delegate)
        {
            lock (Level2Lock)
            {
                bool stop = false;
                for (ulong i = 0; i < Count && !stop; i++)
                    @delegate(Get(i), ref stop, ref i);
            }
        }

        public List() => Initialize();
        public List(ulong capacity, bool expandable) => Initialize(capacity, expandable);
        public List(IEnumerable<T> starting) : this() => PushTailRange(starting);

        public T this[ulong index]
        {
            get => Get(index);
            set => Set(index, value);
        }

        public void PushHead(T item)
        {
            lock (Level3Lock)
            {
                EnsureCapacity(Count + 1);
                RangeCopy(array, 0, 1, Count);
                Set(0, item);
            }
        }

        public void PushHeadRange(IEnumerable<T> array)
        {
            lock (Level4Lock)
            {
                foreach (T item in array)
                    PushHead(item);
            }
        }

        public void PushTail(T item)
        {
            lock (Level3Lock)
            {
                EnsureCapacity(Count + 1);
                Set(Count - 1, item);
            }
        }

        public void PushTailRange(IEnumerable<T> array)
        {
            lock (Level4Lock)
            {
                foreach (T item in array)
                    PushTail(item);
            }
        }

        public T PopHead()
        {
            T item;
            lock (Level3Lock)
            {
                CheckIndex(0);
                item = Get(0);
                if (Count > 1) RangeCopy(array, 1, 0, Count - 1);
                ShrinkCapacity(Count - 1);
            }
            return item;
        }

        public T[] PopHeadRange(ulong count)
        {
            T[] ret;
            lock (Level3Lock)
            {
                unchecked { CheckIndex(count - 1); }
                ret = new T[count];
                RangeCopy(ret, 0, 0, count);
                RangeCopy(array, count, 0, count);
                ShrinkCapacity(Count - count);
            }
            return ret;
        }

        public T PopTail()
        {
            T item;
            lock (Level3Lock)
            {
                unchecked { CheckIndex(Count - 1); }
                item = Get(Count - 1);
                ShrinkCapacity(Count - 1);
            }
            return item;
        }

        public T[] PopTailRange(ulong count)
        {
            T[] ret;
            lock (Level3Lock)
            {
                unchecked { CheckIndex(Count - count); }
                ret = new T[count];
                RangeCopy(ret, Count - count, 0, count);
                ShrinkCapacity(Count - count);
            }
            return ret;
        }

        public void Insert(ulong index, T item)
        {
            lock (Level3Lock)
            {
                CheckIndex(index, true);
                EnsureCapacity(Count + 1);
                RangeCopy(array, index, index + 1, Count - index);
                Set(index, item);
            }
        }

        public bool Remove(T item)
        {
            ulong? index = IndexOf(item);
            if (index == null) return false;
            RemoveAt(index.Value);
            return true;
        }

        public void RemoveAt(ulong index)
        {
            lock (Level3Lock)
            {
                CheckIndex(index);
                RangeCopy(array, index + 1, index, Count - index - 1);
                ShrinkCapacity(Count - 1);
            }
        }

        public void RemoveRange(ulong index, ulong count)
        {
            lock (Level3Lock)
            {
                CheckIndex(index);
                CheckIndex(index + count, true);
                if (index + count < Count)
                    RangeCopy(array, index + count, index, Count - count - index);
                ShrinkCapacity(Count - count);
            }
        }

        public void RemoveAll(Func<T, bool> selector)
        {
            lock (Level3Lock)
                RunDelegate((T a, ref bool b, ref ulong i) =>
                {
                    if (selector(a)) i--;
                });
        }

        public ulong? IndexOf(T item)
        {
            ulong index = 0;
            bool found = false;
            lock (Level3Lock)
                RunDelegate((T a, ref bool b, ref ulong i) =>
                {
                    index++;
                    if (!item.Equals(a)) return;
                    found = b = true;
                });
            return found ? index - 1 : null as ulong?;
        }

        public void ForEach(Action<T> action)
        {
            lock (Level3Lock) RunDelegate((T a, ref bool b, ref ulong i) => action(a));
        }

        public bool Contains(Func<T, bool> selector)
        {
            bool value = false;
            lock (Level3Lock)
                RunDelegate((T a, ref bool b, ref ulong i) =>
                {
                    if (!selector(a)) return;
                    value = b = true;
                });
            return value;
        }

        public string Join(string delimiter = ",")
        {
            string s = "";
            lock (Level4Lock)
            {
                for (ulong i = 0; i < Count; i++)
                    s += (i == 0 ? "" : delimiter) + Get(i);
            }
            return s;
        }

        public T[] CopyToArray()
        {
            T[] ret;
            lock (Level4Lock)
            {
                ret = new T[Count];
                if (Count > 0) RangeCopy(ret, 0, 0, Count);
            }
            return ret;
        }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public class Enumerator : IEnumerator<T>
        {
            internal Enumerator(List<T> source)
            {
                Source = source;
                Monitor.Wait(Source.Level4Lock);
                Monitor.Enter(Source.Level4Lock);
            }

            private List<T> Source { get; }
            private ulong Index { get; set; }
            public T Current => Source.Get(Index);
            object IEnumerator.Current => Source.Get(Index);

            public void Dispose() => Monitor.Exit(Source.Level4Lock);
            public bool MoveNext()
            {
                Index++;
                return Source.Count > Index;
            }
            public void Reset() => Index = 0;
        }
    }
#endif
}
