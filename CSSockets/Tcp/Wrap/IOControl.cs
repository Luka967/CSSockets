using System;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CSSockets.Tcp.Wrap
{
    internal static class IOControl
    {
        public static int SocketsPerThread = 200;
        public static int PollTimeMillisecs = 100;

        private static readonly List<IOThread> threads = new List<IOThread>();
        private static readonly object m_lock = new object();

        internal static IOThread GetBest()
        {
            lock (m_lock)
            {
                IOThread best = null;
                for (int i = 0; i < threads.Count; i++)
                {
                    if (threads[i].Closing) continue;
                    if (threads[i].SocketCount >= SocketsPerThread) continue;
                    if (best == null || best.SocketCount < threads[i].SocketCount)
                        best = threads[i];
                }
                return best ?? Create();
            }
        }

        internal static IOThread Create()
        {
            IOThread thread = new IOThread();
            threads.Add(thread);
            return thread;
        }
        internal static bool Remove(IOThread thread)
        {
            lock (m_lock) return threads.Remove(thread);
        }
    }
    internal class IOThread
    {
        public volatile int SocketCount = 0;
        public volatile int ServerCount = 0;
        public volatile int ClientCount = 0;
        public volatile bool Closing = false;
        public volatile bool GotFirst = false;
        public Thread Thread = null;

        private List<SocketWrapper>                      WrapperList  = new List      <SocketWrapper>();
        private Dictionary<Socket       , SocketWrapper> NativeLookup = new Dictionary<Socket       , SocketWrapper>();
        private Dictionary<SocketWrapper, Connection>    ClientLookup = new Dictionary<SocketWrapper, Connection>();
        private Dictionary<SocketWrapper, Listener>      ServerLookup = new Dictionary<SocketWrapper, Listener>();

        private ConcurrentQueue<IOOperation> OperationQueue = new ConcurrentQueue<IOOperation>();
        public void Enqueue(IOOperation op) => OperationQueue.Enqueue(op);

        private IPEndPoint Resolve(EndPoint endPoint)
        {
            if (endPoint is IPEndPoint) return endPoint as IPEndPoint;
            else if (endPoint is DnsEndPoint)
            {
                IPAddress[] addresses = Dns.GetHostAddresses((endPoint as DnsEndPoint).Host);
                if (addresses.Length == 0) return null;
                return new IPEndPoint(addresses[0].IsIPv4MappedToIPv6 ? addresses[0].MapToIPv4() : addresses[0], (endPoint as DnsEndPoint).Port);
            }
            return null;
        }
        private void Bind(SocketWrapper w = null, Connection c = null, Listener l = null)
        {
            if (w != null)
            {
                GotFirst = true;
                SocketCount++;
                WrapperList.Add(w);
                NativeLookup.Add(w.Socket, w);
            }
            if (c != null) { ClientCount++; ClientLookup.Add(c.Base, c); }
            if (l != null) { ServerCount++; ServerLookup.Add(l.Base, l); }
        }
        private void Unbind(SocketWrapper w = null)
        {
            if (w != null)
            {
                SocketCount--;
                WrapperList.Remove(w);
                NativeLookup.Remove(w.Socket);
                w.BoundThread = null;
                w.State = WrapperState.Destroyed;
                w.WrapperOnUnbind?.Invoke();
                if (ClientLookup.Remove(w, out Connection connection))
                {
                    ClientCount--;
                    connection._EndReadable();
                    connection._EndWritable();
                } 
                if (ServerLookup.Remove(w)) ServerCount--;
            }
        }

        public IOThread() => (Thread = new Thread(Loop) { Name = "TCP I/O thread" }).Start();

        private void Loop()
        {
            List<Socket>
                pollR = new List<Socket>(),
                pollW = new List<Socket>(),
                pollE = new List<Socket>();

            while (true)
            {
                while (OperationQueue.TryDequeue(out IOOperation op))
                {
                    if (op.Type != IOOperationType.WrapperBind && !WrapperList.Contains(op.Callee))
                        continue;
                    ProcessIOOperation(op);
                }

                if (WrapperList.Count == 0 && GotFirst) break;

                pollR.Clear(); pollW.Clear(); pollE.Clear();

                for (int i = 0; i < WrapperList.Count; i++)
                {
                    SocketWrapper w = WrapperList[i];
                    Socket s = w.Socket;
                    if (w.State == WrapperState.ClientOpen ||
                        w.State == WrapperState.ClientReadonly ||
                        w.State == WrapperState.ClientWriteonly)
                        ClientCheckTimeout(w);
                    switch (w.State)
                    {
                        case WrapperState.ServerListening:
                            pollR.Add(s);
                            break;
                        case WrapperState.ClientConnecting:
                            pollW.Add(s); pollE.Add(s);
                            break;
                        case WrapperState.ClientOpen:
                            pollR.Add(s);
                            if (ClientLookup[w].BufferedWritable > 0) pollW.Add(s);
                            break;
                        case WrapperState.ClientReadonly:
                            if (!ClientLookup[w].WritableEnded && ClientLookup[w].BufferedWritable > 0) pollW.Add(s);
                            else if (!ClientLookup[w].WritableEnded)
                            {
                                SocketError code = ClientShutdownSend(s);
                                if (code != SocketError.Success) { BreakIOOperation(w, code); break; }
                                ClientLookup[w]._EndWritable();
                            }
                            pollR.Add(s);
                            break;
                        case WrapperState.ClientWriteonly:
                            if (ClientLookup[w].BufferedWritable > 0) pollW.Add(s);
                            break;
                        case WrapperState.ClientLastWrite:
                            if (!ClientLookup[w].WritableEnded && ClientLookup[w].BufferedWritable > 0)
                                pollW.Add(s);
                            else
                            {
                                SocketError code = ClientShutdownSend(s);
                                if (code != SocketError.Success) { BreakIOOperation(w, code); break; }
                                ClientLookup[w]._EndWritable();
                                w.State = WrapperState.ClientClosed;
                            }
                            break;
                        case WrapperState.ClientClosed:
                            w.ClientOnClose?.Invoke();
                            Unbind(w); i--;
                            break;
                    }
                }

                if (pollR.Count == 0 && pollW.Count == 0 && pollE.Count == 0)
                {
                    Thread.Sleep(IOControl.PollTimeMillisecs);
                    continue;
                }
                else Socket.Select(pollR, pollW, pollE, IOControl.PollTimeMillisecs);

                if (pollR.Count == 0 && pollW.Count == 0 && pollE.Count == 0) continue;

                for (int i = 0; i < pollR.Count; i++)
                {
                    Socket s = pollR[i];
                    SocketWrapper w = NativeLookup[s];
                    switch (w.State)
                    {
                        case WrapperState.ServerListening: ServerAcceptNew(w); break;
                        case WrapperState.ClientOpen:
                        case WrapperState.ClientReadonly:
                            ClientReceive(w);
                            break;
                        default: throw new Exception();
                    }
                }
                for (int i = 0; i < pollW.Count; i++)
                {
                    Socket s = pollW[i];
                    SocketWrapper w = NativeLookup[s];
                    switch (w.State)
                    {
                        case WrapperState.ClientConnecting:
                            w.State = WrapperState.ClientOpen;
                            w.ClientOnConnect?.Invoke();
                            break;
                        case WrapperState.ClientOpen:
                        case WrapperState.ClientWriteonly:
                        case WrapperState.ClientReadonly:  // last write
                        case WrapperState.ClientLastWrite: // last write
                            ClientSend(w);
                            break;
                        default: throw new Exception();
                    }
                }
                for (int i = 0; i < pollE.Count; i++)
                {
                    Socket s = pollE[i];
                    SocketWrapper w = NativeLookup[s];
                    switch (w.State)
                    {
                        case WrapperState.ClientConnecting:
                            BreakIOOperation(w, (SocketError)s.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error));
                            break;
                        default: throw new Exception();
                    }
                }
            }
            IOControl.Remove(this);
        }

        private bool ProcessIOOperation(IOOperation op)
        {
            IPEndPoint resolved;
            switch (op.Type)
            {
                case IOOperationType.Noop:
                    return SucceedIOOperation(op);

                case IOOperationType.WrapperBind:
                    if (op.AdvanceFrom != WrapperState.Unset)
                        return false;
                    Bind(op.Callee);
                    return SucceedIOOperation(op);

                case IOOperationType.WrapperAddServer:
                    if (op.AdvanceFrom != WrapperState.Dormant)
                        return BreakIOOperation(op);
                    if (op.User_2 == null) return FailIOOperation(op);
                    Bind(null, null, op.User_2);
                    op.Callee.Type = WrapperType.Server;
                    return SucceedIOOperation(op);

                case IOOperationType.WrapperAddClient:
                    if (op.AdvanceFrom != WrapperState.Dormant)
                        return BreakIOOperation(op);
                    if (op.User_1 == null) return FailIOOperation(op);
                    Bind(null, op.User_1);
                    op.Callee.Type = WrapperType.Client;
                    return SucceedIOOperation(op);

                case IOOperationType.ServerLookup:
                    if (op.AdvanceFrom != WrapperState.ServerWaitBind &&
                        op.AdvanceFrom != WrapperState.ServerBound)
                        return BreakIOOperation(op);
                    if (op.Lookup == null) return FailIOOperation(op);
                    resolved = Resolve(op.Lookup);
                    if (resolved == null) return FailIOOperation(op);
                    op.Callee.Local = resolved;
                    return SucceedIOOperation(op);

                case IOOperationType.ServerListen:
                    if (op.AdvanceFrom != WrapperState.ServerBound)
                        return BreakIOOperation(op);
                    if (op.Callee.Local == null) return FailIOOperation(op);
                    op.Socket.ExclusiveAddressUse = op.Callee.ServerExclusive;
                    op.Socket.Bind(op.Callee.Local);
                    op.Socket.Listen(op.Callee.ServerBacklog);
                    return SucceedIOOperation(op);

                case IOOperationType.ServerTerminate:
                    if (op.AdvanceFrom != WrapperState.ServerListening)
                        return BreakIOOperation(op);
                    op.Socket.Close();
                    Unbind(op.Callee);
                    return SucceedIOOperation(op);

                case IOOperationType.ClientConnect:
                    if (op.AdvanceFrom != WrapperState.ClientDormant)
                        return BreakIOOperation(op);
                    if (op.Lookup == null) return FailIOOperation(op);
                    resolved = Resolve(op.Lookup);
                    if (resolved == null) return FailIOOperation(op);
                    op.Callee.Remote = resolved;
                    SocketError code = SocketError.Success;
                    try { op.Socket.Connect(resolved); }
                    catch (SocketException ex) { code = ex.SocketErrorCode; }
                    if (code != SocketError.Success && code != SocketError.WouldBlock)
                        BreakIOOperation(op, code);
                    return SucceedIOOperation(op);

                case IOOperationType.ClientShutdown:
                    return ClientShutdown(op, false, true);

                case IOOperationType.ClientTerminate:
                    if (op.AdvanceFrom < WrapperState.ClientDormant)
                        return BreakIOOperation(op);
                    op.Socket.Dispose();
                    Unbind(op.Callee);
                    return SucceedIOOperation(op);

                default: return BreakIOOperation(op);
            }
        }
        private bool SucceedIOOperation(IOOperation op)
        {
            op.Callee.State = op.AdvanceTo;
            return true;
        }
        private bool FailIOOperation(IOOperation op, SocketError error = SocketError.Success)
        {
            if (error != SocketError.Success) op.Callee.WrapperOnSocketError?.Invoke(error);
            op.Callee.State = op.FailAdvanceTo;
            return false;
        }
        private bool BreakIOOperation(IOOperation op, SocketError error = SocketError.Success)
        {
            if (error != SocketError.Success) op.Callee.WrapperOnSocketError?.Invoke(error);
            op.Callee.State = op.BrokenAdvanceTo;
            Unbind(op.Callee);
            return false;
        }
        private bool BreakIOOperation(SocketWrapper w, SocketError error = SocketError.Success)
        {
            if (error != SocketError.Success) w.WrapperOnSocketError?.Invoke(error);
            w.State = WrapperState.Destroyed;
            Unbind(w);
            return false;
        }

        private void ServerAcceptNew(SocketWrapper w)
        {
            SocketWrapper nw = new SocketWrapper(w.Socket.Accept());
            (nw.BoundThread = IOControl.GetBest()).Enqueue(new IOOperation()
            {
                Callee = nw,
                Type = IOOperationType.WrapperBind,
                AdvanceTo = WrapperState.Dormant,
                FailAdvanceTo = WrapperState.Unset,
                BrokenAdvanceTo = WrapperState.Destroyed
            });
            Connection connection = new Connection(nw);
            connection.isComingFromServer = true;
            nw.BoundThread.Enqueue(new IOOperation()
            {
                Callee = nw,
                User_1 = connection,
                Type = IOOperationType.WrapperAddClient,
                AdvanceTo = WrapperState.ClientOpen,
                FailAdvanceTo = WrapperState.Dormant,
                BrokenAdvanceTo = WrapperState.Destroyed
            });
            w.ServerOnConnection?.Invoke(connection);
            connection.isComingFromServer = false;
        }

        private bool ClientReceive(SocketWrapper w)
        {
            Socket s = w.Socket;
            int length = s.Available;
            if (length == 0)
                return ClientShutdown(new IOOperation()
                {
                    Callee = w,
                    Type = IOOperationType.ClientShutdown,
                    BrokenAdvanceTo = WrapperState.ClientClosed,
                    FailAdvanceTo = WrapperState.Destroyed
                }, true, false);
            byte[] data = new byte[length];
            s.Receive(data, 0, length, SocketFlags.None, out SocketError code);
            if (code != SocketError.Success) return BreakIOOperation(w, code);
            ClientLookup[w]._WriteReadable(data);
            ClientExtendTimeout(w);
            return true;
        }
        private bool ClientSend(SocketWrapper w)
        {
            Socket s = w.Socket;
            byte[] data = ClientLookup[w]._ReadWritable(65535);
            s.Send(data, 0, data.Length, SocketFlags.None, out SocketError code);
            if (code != SocketError.Success) return BreakIOOperation(w, code);
            ClientExtendTimeout(w);
            return true;
        }

        private void ClientExtendTimeout(SocketWrapper w)
        {
            w.ClientLastActivity = DateTime.UtcNow;
            w.ClientCalledTimeout = false;
        }
        private bool ClientCheckTimeout(SocketWrapper w)
        {
            if (w.ClientTimeoutAfter == null) return false;
            if (DateTime.UtcNow - w.ClientLastActivity < w.ClientTimeoutAfter) return false;
            if (w.ClientCalledTimeout) return false;
            w.ClientOnTimeout?.Invoke();
            return w.ClientCalledTimeout = true;
        }
        private SocketError ClientShutdownRecv(Socket socket)
        {
            SocketError code = SocketError.Success;
            try { socket.Shutdown(SocketShutdown.Receive); }
            catch (SocketException ex) { code = ex.SocketErrorCode; }
            return code;
        }
        private SocketError ClientShutdownSend(Socket socket)
        {
            SocketError code = SocketError.Success;
            try { socket.Shutdown(SocketShutdown.Send); }
            catch (SocketException ex) { code = ex.SocketErrorCode; }
            return code;
        }
        private bool ClientShutdown(IOOperation op, bool r, bool w)
        {
            if (r && w) return BreakIOOperation(op);
            bool goHalfOpen = op.Callee.ClientAllowHalfOpen;
            SocketError code;
            switch (op.AdvanceFrom)
            {
                case WrapperState.ClientOpen:
                    if (r)
                    {
                        code = ClientShutdownRecv(op.Socket);
                        if (code != SocketError.Success) return BreakIOOperation(op, code);
                        op.AdvanceTo = goHalfOpen ? WrapperState.ClientWriteonly : WrapperState.ClientLastWrite;
                        ClientLookup[op.Callee]._EndReadable();
                    }
                    else if (w) op.AdvanceTo = goHalfOpen ? WrapperState.ClientReadonly : WrapperState.ClientLastWrite;
                    ClientExtendTimeout(op.Callee);
                    return SucceedIOOperation(op);

                case WrapperState.ClientReadonly:
                    if (w) return BreakIOOperation(op);
                    code = ClientShutdownRecv(op.Socket);
                    if (code != SocketError.Success) return BreakIOOperation(op, code);
                    ClientLookup[op.Callee]._EndReadable();
                    op.AdvanceTo = WrapperState.ClientLastWrite;
                    ClientExtendTimeout(op.Callee);
                    return SucceedIOOperation(op);

                case WrapperState.ClientWriteonly:
                    if (r) return BreakIOOperation(op);
                    op.AdvanceTo = WrapperState.ClientLastWrite;
                    ClientExtendTimeout(op.Callee);
                    return SucceedIOOperation(op);

                default:
                    return BreakIOOperation(op);
            }
        }
    }
}
