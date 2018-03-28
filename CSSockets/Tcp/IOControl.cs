using System;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CSSockets.Tcp
{
    public static class IOControl
    {
        public static int OperationsPerThread = 2147483647;
        public static int SocketsPerThread = 200;
        public static int PollTimeMillisecs = 1;

        private static readonly List<IOThread> threads = new List<IOThread>();
        private static readonly object m_lock = new object();

        public static int ThreadCount
        {
            get
            {
                lock (m_lock) return threads.Count;
            }
        }
        public static int SocketCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = 0;
                    for (int i = 0; i < threads.Count; i++) count += threads[i].SocketCount;
                    return count;
                }
            }
        }
        public static int ConnectionCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = 0;
                    for (int i = 0; i < threads.Count; i++) count += threads[i].ClientCount;
                    return count;
                }
            }
        }
        public static int ListenerCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = 0;
                    for (int i = 0; i < threads.Count; i++) count += threads[i].ServerCount;
                    return count;
                }
            }
        }

        internal static IOThread GetBest()
        {
            lock (m_lock)
            {
                IOThread best = null;
                for (int i = 0; i < threads.Count; i++)
                {
                    if (threads[i].Closing) continue;
                    if (threads[i].OperationCount >= OperationsPerThread) continue;
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
        public volatile int OperationCount = 0;
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
        public bool Enqueue(IOOperation op)
        {
            OperationCount++;
            OperationQueue.Enqueue(op);
            return true;
        }

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
        private bool Bind(SocketWrapper wrapper)
        {
            SocketCount++;
            WrapperList.Add(wrapper);
            NativeLookup.Add(wrapper.Socket, wrapper);
            return GotFirst = true;
        }
        private bool Bind(Connection connection, SocketWrapper wrapper)
        {
            ClientCount++;
            ClientLookup.Add(wrapper, connection);
            return true;
        }
        private bool Bind(Listener listener, SocketWrapper wrapper)
        {
            ServerCount++;
            ServerLookup.Add(wrapper, listener);
            return true;
        }
        private bool Unbind(SocketWrapper wrapper, SocketError? code = null)
        {
            if (code != null) wrapper.WrapperOnSocketError?.Invoke(code.Value);
            if (!WrapperList.Remove(wrapper)) return false;
            NativeLookup.Remove(wrapper.Socket);
            wrapper.Local = wrapper.Remote = null;
            SocketCount--;
            if (ClientLookup.Remove(wrapper, out Connection connection))
            {
                ClientCount--;
                connection.InternalEndReadable();
                connection.InternalEndWritable();
                wrapper.ClientOnClose?.Invoke();
            }
            if (ServerLookup.Remove(wrapper)) ServerCount--;
            return false;
        }

        public IOThread() => (Thread = new Thread(Loop) { Name = "TCP I/O thread" }).Start();

        private void Loop()
        {
            List<Socket> pollR = new List<Socket>(), pollW = new List<Socket>(), pollE = new List<Socket>();
            while (true)
            {
                while (OperationQueue.TryDequeue(out IOOperation operation))
                    ExecuteOperation(operation);

                pollR.Clear(); pollW.Clear(); pollE.Clear();

                for (int i = 0; i < SocketCount; i++)
                {
                    SocketWrapper wrapper = WrapperList[i];
                    Socket socket = wrapper.Socket;
                    Connection connection;
                    switch (wrapper.State)
                    {
                        case WrapperState.ClientConnecting:
                        case WrapperState.ClientOpen:
                        case WrapperState.ClientReadonly:
                        case WrapperState.ClientWriteonly:
                            ClientCheckTimeout(wrapper);
                            break;
                    }
                    switch (wrapper.State)
                    {
                        case WrapperState.ServerListening:
                            pollR.Add(socket);
                            break;
                        case WrapperState.ClientConnecting:
                            pollW.Add(socket);
                            pollE.Add(socket);
                            break;
                        case WrapperState.ClientOpen:
                            pollR.Add(socket);
                            connection = ClientLookup[wrapper];
                            if (connection.BufferedWritable > 0)
                                pollW.Add(socket);
                            break;
                        case WrapperState.ClientReadonly:
                            connection = ClientLookup[wrapper];
                            if (!connection.WritableEnded && connection.BufferedWritable > 0)
                                pollW.Add(socket);
                            else if (!connection.WritableEnded)
                                if (!ClientEndWritable(wrapper)) { i--; break; }
                            pollR.Add(socket);
                            break;
                        case WrapperState.ClientWriteonly:
                            connection = ClientLookup[wrapper];
                            if (connection.BufferedWritable > 0)
                                pollW.Add(socket);
                            break;
                        case WrapperState.ClientLastWrite:
                            connection = ClientLookup[wrapper];
                            if (!connection.WritableEnded && connection.BufferedWritable > 0)
                                pollW.Add(socket);
                            else
                            {
                                if (!connection.WritableEnded) ClientEndWritable(wrapper);
                                Unbind(wrapper);
                                i--;
                            }
                            break;
                    }
                }

                if (SocketCount == 0 && GotFirst) break;

                if (pollR.Count == 0 && pollW.Count == 0 && pollE.Count == 0)
                {
                    Thread.Sleep(IOControl.PollTimeMillisecs);
                    continue;
                }
                else Socket.Select(pollR, pollW, pollE, 1000 * IOControl.PollTimeMillisecs);

                for (int i = 0; i < pollR.Count; i++)
                {
                    Socket socket = pollR[i];
                    SocketWrapper wrapper = NativeLookup[socket];
                    switch (wrapper.State)
                    {
                        case WrapperState.ServerListening:
                            ServerAccept(wrapper);
                            break;
                        case WrapperState.ClientOpen:
                        case WrapperState.ClientReadonly:
                            ClientReceive(wrapper);
                            break;
                    }
                }

                for (int i = 0; i < pollW.Count; i++)
                {
                    Socket socket = pollW[i];
                    SocketWrapper wrapper = NativeLookup[socket];
                    switch (wrapper.State)
                    {
                        case WrapperState.ClientConnecting:
                            ClientOpen(wrapper);
                            break;
                        case WrapperState.ClientOpen:
                        case WrapperState.ClientWriteonly:
                        case WrapperState.ClientLastWrite:
                            ClientSend(wrapper);
                            break;
                    }
                }

                for (int i = 0; i < pollE.Count; i++)
                {
                    Socket socket = pollE[i];
                    SocketWrapper wrapper = NativeLookup[socket];
                    switch (wrapper.State)
                    {
                        case WrapperState.ClientConnecting:
                            Unbind(wrapper, (SocketError)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error));
                            break;
                    }
                }
            }
            Closing = true;
            IOControl.Remove(this);
        }

        private bool SucceedOperation(SocketWrapper wrapper, WrapperState? state = null)
        {
            if (state != null) wrapper.State = state.Value;
            return true;
        }
        private bool FailOperation(SocketWrapper wrapper, WrapperState state)
        {
            wrapper.State = state;
            return false;
        }
        private bool ExecuteOperation(IOOperation operation)
        {
            OperationCount--;
            SocketWrapper wrapper = operation.Callee;
            Socket socket = wrapper.Socket;
            IPEndPoint endPoint;
            SocketError code = SocketError.Success;
            switch (operation.Type)
            {
                case IOOperationType.Noop:
                    return SucceedOperation(wrapper);

                case IOOperationType.WrapperBind:
                    if (operation.AdvanceFrom != WrapperState.Unset) return Unbind(wrapper);
                    return Bind(wrapper)
                        ? SucceedOperation(wrapper, WrapperState.Dormant)
                        : FailOperation(wrapper, WrapperState.Unset);

                case IOOperationType.WrapperAddServer:
                    return Bind(operation.Listener, wrapper)
                        ? SucceedOperation(wrapper, WrapperState.ServerDormant)
                        : FailOperation(wrapper, WrapperState.Dormant);

                case IOOperationType.WrapperAddClient:
                    if (operation.AdvanceFrom != WrapperState.Dormant) return Unbind(wrapper);
                    return Bind(operation.Connection, wrapper)
                        ? SucceedOperation(wrapper, WrapperState.ClientDormant)
                        : FailOperation(wrapper, WrapperState.Dormant);

                case IOOperationType.ServerLookup:
                    if (operation.AdvanceFrom != WrapperState.ServerDormant) return Unbind(wrapper);
                    wrapper.Local = endPoint = Resolve(operation.Lookup);
                    if (endPoint == null) return FailOperation(wrapper, WrapperState.ServerDormant);
                    return SucceedOperation(wrapper, WrapperState.ServerBound);

                case IOOperationType.ServerListen:
                    if (operation.AdvanceFrom == WrapperState.ServerDormant) return FailOperation(wrapper, WrapperState.ServerDormant);
                    else if (operation.AdvanceFrom != WrapperState.ServerBound) return Unbind(wrapper);
                    try
                    {
                        socket.ExclusiveAddressUse = wrapper.ServerExclusive;
                        socket.Bind(wrapper.Local);
                        socket.Listen(wrapper.ServerBacklog);
                    }
                    catch (SocketException ex) { code = ex.SocketErrorCode; }
                    return code != SocketError.Success
                        ? Unbind(wrapper, code)
                        : SucceedOperation(wrapper, WrapperState.ServerListening);

                case IOOperationType.ServerTerminate:
                    return Unbind(wrapper);

                case IOOperationType.ClientOpen:
                    if (operation.AdvanceFrom != WrapperState.ClientDormant) return Unbind(wrapper);
                    ClientOpen(wrapper, operation.Referer, operation.Connection);
                    return true;

                case IOOperationType.ClientConnect:
                    if (operation.AdvanceFrom != WrapperState.ClientDormant) return Unbind(wrapper);
                    wrapper.Remote = endPoint = Resolve(operation.Lookup);
                    if (endPoint == null) return Unbind(wrapper, SocketError.NetworkUnreachable);
                    try { socket.Connect(endPoint); }
                    catch (SocketException ex) { code = ex.SocketErrorCode; }
                    return (code != SocketError.Success && code != SocketError.WouldBlock)
                        ? Unbind(wrapper, code)
                        : SucceedOperation(wrapper, WrapperState.ClientConnecting) && ClientExtendTimeout(wrapper);

                case IOOperationType.ClientShutdown:
                    return ClientShutdown(wrapper, false, true);

                case IOOperationType.ClientTerminate:
                    return Unbind(wrapper);

                default: throw new Exception();
            }
        }
        
        private bool ServerAccept(SocketWrapper wrapper)
        {
            do
            {
                SocketWrapper newWrapper = new SocketWrapper(wrapper.Socket.Accept());
                Connection connection = new Connection(newWrapper);
                newWrapper.WrapperBind();
                newWrapper.WrapperAddClient(connection);
                newWrapper.ClientOpen(wrapper, connection);
            }
            while (wrapper.Socket.Poll(0, SelectMode.SelectRead));
            return true;
        }

        private bool ClientExtendTimeout(SocketWrapper wrapper)
        {
            wrapper.ClientLastActivity = DateTime.UtcNow;
            wrapper.ClientCalledTimeout = false;
            return true;
        }
        private bool ClientCheckTimeout(SocketWrapper wrapper)
        {
            if (wrapper.ClientTimeoutAfter == null || wrapper.ClientCalledTimeout) return true;
            if (DateTime.UtcNow - wrapper.ClientLastActivity < wrapper.ClientTimeoutAfter) return true;
            wrapper.ClientOnTimeout?.Invoke();
            wrapper.ClientCalledTimeout = true;
            return true;
        }
        private bool ClientOpen(SocketWrapper wrapper)
        {
            SucceedOperation(wrapper, WrapperState.ClientOpen);
            wrapper.Local = Resolve(wrapper.Socket.LocalEndPoint);
            wrapper.Remote = Resolve(wrapper.Socket.RemoteEndPoint);
            wrapper.ClientOnConnect?.Invoke();
            return ClientExtendTimeout(wrapper);
        }
        private bool ClientOpen(SocketWrapper wrapper, SocketWrapper referer, Connection connection)
        {
            SucceedOperation(wrapper, WrapperState.ClientOpen);
            wrapper.Local = Resolve(wrapper.Socket.LocalEndPoint);
            wrapper.Remote = Resolve(wrapper.Socket.RemoteEndPoint);
            referer.ServerOnConnection?.Invoke(connection);
            return ClientExtendTimeout(wrapper);
        }
        private bool ClientReceive(SocketWrapper wrapper)
        {
            int available = wrapper.Socket.Available;
            if (available == 0) return ClientShutdown(wrapper, true, false);
            byte[] data = new byte[available];
            wrapper.Socket.Receive(data, 0, available, SocketFlags.None, out SocketError code);
            if (code == SocketError.Success)
                return ClientLookup[wrapper].Internal(data) && ClientExtendTimeout(wrapper);
            else return Unbind(wrapper, code);
        }
        private bool ClientSend(SocketWrapper wrapper)
        {
            byte[] data = ClientLookup[wrapper].InternalReadWritable(65536);
            wrapper.Socket.Send(data, 0, data.Length, SocketFlags.None, out SocketError code);
            if (code != SocketError.Success) return Unbind(wrapper, code);
            return ClientExtendTimeout(wrapper);
        }
        private SocketError ClientSocketShutdown(Socket socket, SocketShutdown how)
        {
            try { socket.Shutdown(how); }
            catch (SocketException ex) { return ex.SocketErrorCode; }
            return SocketError.Success;
        }
        private bool ClientEndReadable(SocketWrapper wrapper)
        {
            SocketError code = ClientSocketShutdown(wrapper.Socket, SocketShutdown.Receive);
            if (code != SocketError.Success) return Unbind(wrapper, code);
            ClientLookup[wrapper].InternalEndReadable();
            wrapper.ClientOnShutdown?.Invoke();
            return true;
        }
        private bool ClientEndWritable(SocketWrapper wrapper)
        {
            SocketError code = ClientSocketShutdown(wrapper.Socket, SocketShutdown.Send);
            if (code != SocketError.Success) return Unbind(wrapper, code);
            ClientLookup[wrapper].InternalEndWritable();
            return true;
        }
        private bool ClientShutdown(SocketWrapper wrapper, bool r, bool w)
        {
            SocketError code = SocketError.Success;
            bool halfOpen = wrapper.ClientAllowHalfOpen;
            switch (wrapper.State)
            {
                case WrapperState.ClientOpen:
                    if (r && w) return Unbind(wrapper);
                    if (r)
                        return
                            ClientEndReadable(wrapper)
                            && SucceedOperation(wrapper, halfOpen ? WrapperState.ClientWriteonly : WrapperState.ClientLastWrite);
                    else return SucceedOperation(wrapper, halfOpen ? WrapperState.ClientReadonly : WrapperState.ClientLastWrite);

                case WrapperState.ClientReadonly:
                    if (w) return Unbind(wrapper);
                    code = ClientSocketShutdown(wrapper.Socket, SocketShutdown.Receive);
                    if (code != SocketError.Success) return Unbind(wrapper, code);
                    ClientLookup[wrapper].InternalEndReadable();
                    wrapper.ClientOnShutdown?.Invoke();
                    return SucceedOperation(wrapper, WrapperState.ClientLastWrite);

                case WrapperState.ClientWriteonly:
                    if (r) return Unbind(wrapper);
                    return SucceedOperation(wrapper, WrapperState.ClientLastWrite);

                default: return Unbind(wrapper);
            }
        }
    }
}
