using System;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CSSockets.Tcp
{
    public static class SocketIOHandler
    {
        public static int SocketsPerThread = 32;
        public static int ThreadSleepMilliseconds = 1;

        private static readonly object Sync = new object();
        private static readonly List<IOThread> Threads = new List<IOThread>();

        internal static IOThread AddNew()
        {
            IOThread thread = new IOThread();
            Threads.Add(thread);
            return thread;
        }
        internal static bool Remove(IOThread thread)
        {
            lock (Sync) return Threads.Remove(thread);
        }
        public static IOThread GetBest()
        {
            lock (Sync)
            {
                IOThread best = null;
                for (int i = 0; i < Threads.Count; i++)
                {
                    IOThread curr = Threads[i];
                    if (curr.SocketCount >= SocketsPerThread || (curr.SocketCount == 0 && curr.GotFirstSocket))
                        continue;
                    if (best == null || best.SocketCount > curr.SocketCount) best = curr;
                }
                return best ?? AddNew();
            }
        }

        public class IOThread
        {
            private readonly List<TcpSocket> Sockets = new List<TcpSocket>();
            private readonly Dictionary<Socket, TcpSocket> Wrappers = new Dictionary<Socket, TcpSocket>();

            private readonly ConcurrentQueue<TcpSocket> Qopen = new ConcurrentQueue<TcpSocket>();
            private readonly ConcurrentQueue<TcpSocket> QendWrite = new ConcurrentQueue<TcpSocket>();
            private readonly ConcurrentQueue<TcpSocket> Qclose = new ConcurrentQueue<TcpSocket>();
            private readonly ConcurrentQueue<TcpSocket> Qterminate = new ConcurrentQueue<TcpSocket>();
            private Thread Wthread;

            public bool GotFirstSocket { get; private set; } = false;
            public int SocketCount => Sockets.Count;
            public int RegisterQueueLength => Qopen.Count;
            public int EndWriteQueueLength => QendWrite.Count;
            public int CloseQueueLength => Qclose.Count;
            public int TerminateQueueLength => Qterminate.Count;

            public bool Open(TcpSocket socket)
            {
                if (socket.HqueuedOpen) return false;
                Qopen.Enqueue(socket); return socket.HqueuedOpen = true;
            }
            public bool EndWrite(TcpSocket socket)
            {
                if (socket.HqueuedEndW) return false;
                QendWrite.Enqueue(socket); return socket.HqueuedEndW = true;
            }
            public bool Close(TcpSocket socket)
            {
                if (socket.HqueuedClose) return false;
                Qclose.Enqueue(socket); return socket.HqueuedClose = true;
            }
            public bool Terminate(TcpSocket socket)
            {
                if (socket.HqueuedTerm) return false;
                Qterminate.Enqueue(socket); return socket.HqueuedTerm = true;
            }

            public IOThread()
            {
                Wthread = new Thread(WthreadRun) { Name = "TCP I/O Thread" };
                Wthread.Start();
            }

            private void BindNew(TcpSocket socket)
            {
                Sockets.Add(socket);
                Wrappers.Add(socket.Base, socket);
            }
            private void Remove(TcpSocket socket, bool should)
            {
                if (!should) return;
                Sockets.Remove(socket);
                Wrappers.Remove(socket.Base);
            }
            private void WthreadRun()
            {
                List<Socket> r = new List<Socket>();
                List<Socket> w = new List<Socket>();
                List<Socket> e = new List<Socket>();
                List<Socket> re = new List<Socket>();
                List<(Socket s, SocketError se)> ee = new List<(Socket, SocketError)>();
                while (true)
                {
                    while (Qterminate.TryDequeue(out TcpSocket ts))
                    {
                        ts.HqueuedTerm = false;
                        Remove(ts, ts.Control(TcpSocketOp.Terminate));
                    }
                    while (Qclose.TryDequeue(out TcpSocket ts))
                    {
                        ts.HqueuedClose = false;
                        Remove(ts, ts.Control(TcpSocketOp.Close));
                    }
                    while (QendWrite.TryDequeue(out TcpSocket ts))
                    {
                        if (!ts.WritableEnded && ts.BufferedWritable > 0) continue;
                        ts.HqueuedEndW = false;
                        ts.Control(TcpSocketOp.EndWrite);
                    }
                    while (Qopen.TryDequeue(out TcpSocket ts))
                    {
                        ts.HqueuedOpen = false;
                        GotFirstSocket = true;
                        BindNew(ts);
                        ts.Base.NoDelay = true;
                        ts.Control(TcpSocketOp.Open);
                    }

                    if (Sockets.Count == 0 && GotFirstSocket) break;

                    r.Clear(); w.Clear(); e.Clear();
                    re.Clear(); ee.Clear();

                    foreach (Socket s in Wrappers.Keys)
                    {
                        TcpSocket ts = Wrappers[s];
                        if (!ts.WritableEnded && ts.BufferedWritable > 0)
                        {
                            if (ts.HqueuedEndW) QendWrite.Enqueue(ts);
                            w.Add(s);
                        }
                        else if (ts.CanTimeout && DateTime.UtcNow - ts.HlastActivity > ts.TimeoutAfter)
                            if (ts.FireTimeout()) EndWrite(ts);
                        if (!ts.ReadableEnded) r.Add(s);
                    }

                    if (r.Count == 0 && w.Count == 0) continue;

                    Socket.Select(r, w, e, 1000 * ThreadSleepMilliseconds);

                    if (r.Count == 0 && w.Count == 0) continue;

                    for (int i = 0; i < r.Count; i++)
                    {
                        Socket s = r[i]; TcpSocket ts = Wrappers[s];
                        if (s.Available == 0) { re.Add(s); continue; }
                        byte[] data = new byte[s.Available];
                        s.Receive(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code == SocketError.Success) ts.WriteReadable(data);
                        else ee.Add((s, code));
                    }
                    for (int i = 0; i < w.Count; i++)
                    {
                        Socket s = w[i]; TcpSocket ts = Wrappers[s];
                        byte[] data = ts.ReadWritable();
                        s.Send(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code != SocketError.Success) ee.Add((s, code));
                    }

                    for (int i = 0; i < re.Count; i++)
                    {
                        TcpSocket ts = Wrappers[re[i]];
                        Remove(ts, ts.Control(TcpSocketOp.EndRead));
                    }
                    for (int i = 0; i < ee.Count; i++)
                    {
                        TcpSocket ts = Wrappers[ee[i].s];
                        Remove(ts, ts.Control(TcpSocketOp.Throw, ee[i].se));
                    }
                }
                SocketIOHandler.Remove(this);
            }
        }
    }
}
