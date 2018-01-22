//#define DEBUG_TCPIO

using System;
using CSSockets.Base;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using NonBlockingSocketQueue = System.Collections.Generic.Queue<CSSockets.Tcp.TcpSocket>;

namespace CSSockets.Tcp
{
    public static class TcpSocketIOHandler
    {
        public static int PollTime = 1000;
        public static int SocketsPerThread = 48;

        private static List<IOThread> _threads = new List<IOThread>();
        public static ReadOnlyCollection<IOThread> Threads
        {
            get
            {
                IOThread[] arr;
                lock (_threadModifyLock) arr = _threads.ToArray();
                return new ReadOnlyCollection<IOThread>(arr);
            }
        }
        private static object _threadModifyLock = new object();
        public static int SocketCount
        {
            get
            {
                int ret = 0;
                lock (_threadModifyLock) for (int i = 0; i < _threads.Count; i++)
                        ret += _threads[i].SocketCount;
                return ret;
            }
        }

        public static IOThread Enqueue(TcpSocket socket)
        {
            IOThread best = null;
            lock (_threadModifyLock)
            {
                for (int i = 0; i < _threads.Count; i++)
                {
                    IOThread curr = _threads[i];
                    if (best == null || best.SocketCount > curr.SocketCount)
                        best = curr;
                }
                if (best == null || best.SocketCount > SocketsPerThread)
                {
                    IOThread empty = new IOThread();
                    _threads.Add(empty);
                    best = empty;
                }
            }
            return best.EnqueueNew(socket);
        }

        private static void OnThreadEnd(IOThread t)
        {
            lock (_threadModifyLock) _threads.Remove(t);
        }

        public class IOThread
        {
            private ConcurrentQueue<TcpSocket> QueuedNew = new ConcurrentQueue<TcpSocket>();
            private NonBlockingSocketQueue QueuedTerminate = new NonBlockingSocketQueue();
            private NonBlockingSocketQueue QueuedClose = new NonBlockingSocketQueue();
            private object terminateLock = new object(), closeLock = new object();

            internal volatile bool Running = true;
            internal volatile int SocketCount = 0;
            internal bool gotFirstSocket = false;

            public IOThread() => new Thread(Poll) { Name = "TCP I/O thread" }.Start();

            public IOThread EnqueueNew(TcpSocket socket)
            {
                if (socket.Ended)
                {
                    gotFirstSocket = true;
                    return this;
                }
                socket.IOHandler = this;
                socket.Base.NoDelay = true;
                socket.Base.Blocking = false;
                QueuedNew.Enqueue(socket);
                return this;
            }

            public IOThread EnqueueTerminate(TcpSocket socket)
            {
                lock (terminateLock)
                {
                    if (socket.IsTerminating) return this;
                    socket.IsTerminating = true;
                    QueuedTerminate.Enqueue(socket);
                }
                return this;
            }

            public IOThread EnqueueClose(TcpSocket socket)
            {
                lock (closeLock)
                {
                    if (socket.IsClosing) return this;
                    socket.IsClosing = true;
                    QueuedClose.Enqueue(socket);
                }
                return this;
            }

            List<Socket> sockets = new List<Socket>();
            List<TcpSocket> nextTimeEnding = new List<TcpSocket>();
            Dictionary<Socket, TcpSocket> wraps = new Dictionary<Socket, TcpSocket>();
            List<Socket> checkR = new List<Socket>(), checkW = new List<Socket>(), checkE = new List<Socket>();
            List<Socket> endR = new List<Socket>();
            List<(Socket, SocketError)> endE = new List<(Socket, SocketError)>();

            private void _AddSocket(TcpSocket ts)
            {
#if DEBUG_TCPIO
                Console.WriteLine("_AddSocket: added a socket (is server: {0})", ts.isServer);
#endif
                gotFirstSocket = true;
                sockets.Add(ts.Base);
                wraps.Add(ts.Base, ts);
                SocketCount++;
                ts.FireOpen();
            }
            private bool _RemoveSocket(TcpSocket ts, bool should)
            {
                if (!should) return false;
#if DEBUG_TCPIO
                Console.WriteLine("_RemoveSocket: removed a socket (is server: {0})", ts.isServer);
#endif
                sockets.Remove(ts.Base);
                wraps.Remove(ts.Base);
                SocketCount--;
                return true;
            }

            private void Poll()
            {
                while (true)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("\nPoll: socket count is {0}", SocketCount);
                    Console.WriteLine("Poll: adding {0}", QueuedNew.Count);
#endif
                    while (QueuedNew.TryDequeue(out TcpSocket ts))
                        _AddSocket(ts);
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: terminating {0}", QueuedTerminate.Count);
#endif
                    while (QueuedTerminate.TryDequeue(out TcpSocket ts))
                    {
                        _RemoveSocket(ts, ts.Control(null, false, true, false, false));
                        ts.IsTerminating = false;
                    }
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: closing {0}", QueuedClose.Count);
#endif
                    while (QueuedClose.TryDequeue(out TcpSocket ts))
                    {
#if DEBUG_TCPIO
                        Console.WriteLine("Poll: will close: {0} (is server: {1})", ts.WritableEnded || ts.OutgoingBuffered == 0, ts.isServer);
#endif
                        if (ts.Ended) ts.IsClosing = false; // already ended during i/o handling
                        else if (!ts.WritableEnded && ts.OutgoingBuffered > 0)
                            // has data to write - schedule for next time
                            nextTimeEnding.Add(ts);
                        else
                        {
                            _RemoveSocket(ts, ts.Control(null, false, false, false, true));
                            ts.IsClosing = false;
                        }
                    }
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: socket count is now {0}", SocketCount);
#endif
                    if (SocketCount == 0 && gotFirstSocket) break;

                    checkR.Clear(); endR.Clear();
                    checkW.Clear();
                    endE.Clear();
                    for (int i = 0, l = nextTimeEnding.Count; i < l; i++)
                        QueuedClose.Enqueue(nextTimeEnding[i]);
                    nextTimeEnding.Clear();

                    for (int i = 0, l = sockets.Count; i < l; i++)
                    {
                        Socket s = sockets[i];
                        TcpSocket ts = wraps[s];
                        bool stillOpen = true;
                        if (!ts.WritableEnded && ts.OutgoingBuffered > 0) checkW.Add(s);
                        else if (!ts.WritableEnded && ts.CanTimeout && DateTime.UtcNow - ts.LastActivityTime > ts.TimeoutAfter)
                            stillOpen = _RemoveSocket(ts, ts.FireTimeout());
                        if (!ts.ReadableEnded && stillOpen) checkR.Add(s);
                    }
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: selecting {0}r {1}w {2}e", checkR.Count, checkW.Count, checkE.Count);
#endif
                    if (checkR.Count == 0 && checkW.Count == 0) continue;

                    Socket.Select(checkR, checkW, checkE, PollTime);
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: select ended; {0}r {1}w {2}e", checkR.Count, checkW.Count, checkE.Count);
#endif
                    if (checkR.Count == 0 && checkW.Count == 0) continue;

                    for (int i = 0, l = checkR.Count; i < l; i++)
                    {
                        Socket s = checkR[i];
                        TcpSocket ts = wraps[s];
                        if (s.Available == 0) { endR.Add(s); continue; }
                        byte[] data = new byte[s.Available];
                        s.Receive(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code == SocketError.Success) ts.WriteIncoming(data);
                        else endE.Add((s, code));
                    }

                    for (int i = 0, l = checkW.Count; i < l; i++)
                    {
                        Socket s = checkW[i];
                        TcpSocket ts = wraps[s];
                        byte[] data = ts.ReadOutgoing();
                        s.Send(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code != SocketError.Success) endE.Add((s, code));
                    }
#if DEBUG_TCPIO
                    Console.WriteLine("Poll: ending {0}r 0w {1}e", endR.Count, endE.Count);
#endif

                    for (int i = 0, l = endR.Count; i < l; i++)
                    {
                        TcpSocket ts = wraps[endR[i]];
                        _RemoveSocket(ts, ts.Control(null, false, false, true, false));
                    }

                    for (int i = 0, l = endE.Count; i < l; i++)
                    {
                        (Socket, SocketError) item = endE[i];
                        TcpSocket ts = wraps[item.Item1];
                        _RemoveSocket(ts, ts.Control(new SocketException((int)item.Item2), false, false, false, false));
                    }
                }
                OnThreadEnd(this);
            }
        }
    }
}