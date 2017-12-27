using System;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace CSSockets.Tcp
{
    internal static class TcpSocketIOHandler
    {
        private const int POLL_TIME = 100;
        private const int THREAD_MAX_SOCKETS = 32;

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
                if (best == null || best.SocketCount > THREAD_MAX_SOCKETS)
                {
                    IOThread empty = new IOThread();
                    _threads.Add(empty);
                    best = empty;
                }
            }
            return best.Enqueue(socket);
        }

        private static void OnThreadEnd(IOThread t)
        {
            lock (_threadModifyLock) _threads.Remove(t);
        }

        internal class IOThread
        {
            private ConcurrentQueue<TcpSocket> QueuedNew = new ConcurrentQueue<TcpSocket>();
            private ConcurrentQueue<TcpSocket> QueuedTerm = new ConcurrentQueue<TcpSocket>();
            private ConcurrentQueue<TcpSocket> QueuedCloseProgress = new ConcurrentQueue<TcpSocket>();

            internal volatile bool Running = true;
            internal volatile int SocketCount = 0;

            public IOThread() => new Thread(PollT) { IsBackground = true, Name = "TCP I/O Thread" }.Start();

            public IOThread Enqueue(TcpSocket socket)
            {
                socket.IOHandler = this;
                QueuedNew.Enqueue(socket);
                return this;
            }

            public IOThread EnqueueTerminate(TcpSocket socket)
            {
                QueuedTerm.Enqueue(socket);
                return this;
            }

            public IOThread EnqueueCloseProgress(TcpSocket socket)
            {
                QueuedCloseProgress.Enqueue(socket);
                return this;
            }

            private void PollT()
            {
                Dictionary<Socket, TcpSocket> streams = new Dictionary<Socket, TcpSocket>();

                List<Socket> recvCheck = new List<Socket>(), sendCheck = new List<Socket>(), errorCheck = new List<Socket>();
                HashSet<Socket> endRead = new HashSet<Socket>(), endWrite = new HashSet<Socket>();
                Dictionary<Socket, SocketError> endError = new Dictionary<Socket, SocketError>();

                while (Running)
                {
                    // insert new
                    while (QueuedNew.TryDequeue(out TcpSocket ts))
                        streams.Add(ts.Base, ts);
                    // terminate requesting
                    while (QueuedTerm.TryDequeue(out TcpSocket ts))
                    {
                        ts.SocketControl(null, false, true, false, false, false);
                        streams.Remove(ts.Base);
                    }
                    // close requesting
                    while (QueuedCloseProgress.TryDequeue(out TcpSocket ts))
                    {
                        if (!ts.WritableEnded) ts.SocketControl(null, false, false, false, false, true);
                        else ts.SocketControl(null, false, false, true, false, false);
                    }

                    // update count & shut down if none
                    if ((SocketCount = streams.Count) == 0) break;

                    // update lists
                    recvCheck.Clear(); sendCheck.Clear(); errorCheck.Clear();
                    recvCheck.AddRange(streams.Keys);
                    // prematurely check if the wrapped socket has buffered data
                    // if any other were to be added Socket.Select would end immediately as the sockets can send data
                    foreach (KeyValuePair<Socket, TcpSocket> v in streams)
                    {
                        TcpSocket ts = v.Value;
                        if (ts.WritableEnded)
                            endWrite.Add(v.Key);
                        else if (ts.OutgoingBuffered > 0)
                            sendCheck.Add(v.Key);
                        else if (ts.CanTimeout && DateTime.UtcNow - ts.LastActivityTime > ts.TimeoutAfter)
                            ts.FireTimeout();
                    }
                    endRead.Clear(); endWrite.Clear(); endError.Clear();

                    // execute long-polling
                    Socket.Select(recvCheck, sendCheck, errorCheck, POLL_TIME);
                    if (recvCheck.Count == 0 && sendCheck.Count == 0) continue;

                    // check receiving
                    for (int i = 0; i < recvCheck.Count; i++)
                    {
                        Socket s = recvCheck[i];
                        TcpSocket ts = streams[s];
                        if (ts.ReadableEnded) { endRead.Add(s); continue; }
                        byte[] data = new byte[s.Available];
                        int len = s.Receive(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code == SocketError.Interrupted || len == 0) endRead.Add(s);
                        else if (code != SocketError.Success) endError.Add(s, code);
                        else ts.WriteIncoming(data);
                    }
                    // check sending
                    for (int i = 0; i < sendCheck.Count; i++)
                    {
                        Socket s = sendCheck[i];
                        TcpSocket ts = streams[s];
                        if (endError.ContainsKey(s)) continue;
                        byte[] data = ts.ReadOutgoing();
                        s.Send(data, 0, data.Length, SocketFlags.None, out SocketError code);
                        if (code == SocketError.Interrupted) endWrite.Add(s);
                        else if (code != SocketError.Success) endError.Add(s, code);
                    }

                    // end readable
                    foreach (Socket s in endRead)
                        if (streams[s].SocketControl(null, false, false, false, true, false))
                            // fully ended
                            streams.Remove(s);
                    // end writable
                    foreach (Socket s in endWrite)
                        if (streams[s].SocketControl(null, false, false, false, false, true))
                            // fully ended
                            streams.Remove(s);
                    // end terminated/crashed
                    foreach (KeyValuePair<Socket, SocketError> s in endError)
                    {
                        streams[s.Key].SocketControl(new SocketException((int)s.Value), false, false, false, false, false);
                        streams.Remove(s.Key);
                    }
                }
                TcpSocketIOHandler.OnThreadEnd(this);
            }
        }
    }
}
