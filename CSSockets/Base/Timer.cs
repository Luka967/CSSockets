#if DEBUG
using System;
using System.Threading;
using System.Collections.Generic;

namespace CSSockets.Base
{
    public class Timeout
    {
        public bool? Executed { get; internal set; } = false;
        public bool Repeated { get; }
        public Delegate Method { get; }
        public object[] Arguments { get; }
        public int TimeoutDuration { get; }

        public Timeout(bool repeated, Delegate method, object[] arguments, int duration)
        {
            Repeated = repeated;
            Method = method;
            Arguments = arguments;
            TimeoutDuration = duration;
        }
    }

    internal class SortedTimeoutList
    {
        private List<Timeout> List { get; }
        private object Sync { get; }

        public SortedTimeoutList()
        {
            List = new List<Timeout>();
            Sync = new object();
        }

        public Timeout First()
        {
            lock (Sync) return List.Count == 0 ? null : List[0];
        }
        public void Add(Timeout timeout)
        {
            lock (Sync)
            {
                int index = 0, count = List.Count;
                for (; index < count; index++)
                    if (timeout.TimeoutDuration <= List[index].TimeoutDuration) break;
                List.Insert(index, timeout);
            }
        }
        public bool Remove(Timeout timeout)
        {
            bool found = false;
            lock (Sync) found = List.Remove(timeout);
            if (found) timeout.Executed = false;
            return found;
        }
        public void ExecuteFirst()
        {
            Timeout timeout;
            lock (Sync)
            {
                timeout = List[0];
                List.RemoveAt(0);
            }
            timeout.Method.DynamicInvoke(timeout.Arguments);
            if (timeout.Repeated) Add(timeout);
            else timeout.Executed = true;
        }
    }

    public class Timer
    {
        private EventWaitHandle EventInterrupt { get; set; }
        private bool InterruptReset { get; set; }
        private Thread Thread { get; set; }
        private SortedTimeoutList List { get; }
        public bool Running { get; set; }

        public Timer() => List = new SortedTimeoutList();

        public void Begin()
        {
            InterruptReset = false;
            EventInterrupt = new EventWaitHandle(false, EventResetMode.ManualReset);
            Thread = new Thread(TimerThread) { IsBackground = true };
            Thread.Start();
        }

        private void Reset()
        {
            InterruptReset = true;
            EventInterrupt.Set();
        }

        public void End()
        {
            Running = false;
            Reset();
        }

        private void TimerThread()
        {
            Running = true;
            DateTime? nextTime = null;
            while (true)
            {
                if (!Running || EventInterrupt.SafeWaitHandle.IsClosed) break;
                if (nextTime == null)
                {
                    Timeout next = List.First();
                    if (next == null) EventInterrupt.WaitOne();
                    else nextTime = DateTime.Now.AddMilliseconds(next.TimeoutDuration);
                }
                if (nextTime != null)
                {
                    TimeSpan timeout = nextTime.Value - DateTime.Now;
                    EventInterrupt.WaitOne(timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero);
                    if (InterruptReset)
                    {
                        InterruptReset = false;
                        EventInterrupt.Reset();
                        nextTime = null;
                        continue;
                    }
                    List.ExecuteFirst();
                    nextTime = null;
                }
            }
            Running = false;
            EventInterrupt.Dispose();
        }

        public Timeout SetTimeout(Delegate method, int timeout, params object[] arguments)
        {
            Timeout newTimeout = new Timeout(false, method, arguments, timeout);
            List.Add(newTimeout);
            Reset();
            return newTimeout;
        }

        public Timeout SetInterval(Delegate method, int timeout, params object[] arguments)
        {
            Timeout newTimeout = new Timeout(true, method, arguments, timeout);
            List.Add(newTimeout);
            Reset();
            return newTimeout;
        }

        public bool Clear(Timeout timeout)
        {
            Reset();
            return List.Remove(timeout);
        }
    }
}
#endif