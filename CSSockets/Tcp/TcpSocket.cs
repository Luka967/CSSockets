//#define DEBUG_TCPIO

using System;
using System.Net;
using System.Net.Sockets;
using CSSockets.Streams;

namespace CSSockets.Tcp
{
    /// <summary>
    /// Represents the states a TcpSocket can have.
    /// </summary>
    public enum TcpSocketState : byte
    {
        Closed = 0,
        Opening = 1,
        Open = 2,
        Closing = 3
    }
    /// <summary>
    /// TcpSocket data flow handler.
    /// </summary>
    public delegate void TcpSocketControlHandler();
    /// <summary>
    /// TcpSocket exception handler.
    /// </summary>
    /// <param name="exception">The exception thrown.</param>
    public delegate void TcpExceptionHandler(SocketException exception);

    /// <summary>
    /// A TCP socket wrapper inheriting the BaseDuplex.
    /// </summary>
    sealed public class TcpSocket : BaseDuplex
    {
        public TcpSocketState State { get; internal set; } = TcpSocketState.Closed;
        public Socket Base { get; }
        public IPAddress RemoteAddress { get; private set; }

        internal TcpSocketIOHandler.IOThread IOHandler { get; set; }

        public event TcpSocketControlHandler OnOpen;
        public event TcpExceptionHandler OnError;
        public event TcpSocketControlHandler OnTimeout;
        public event TcpSocketControlHandler OnEnd;
        public event TcpSocketControlHandler OnClose;

        public bool CanTimeout { get; set; }
        public TimeSpan TimeoutAfter { get; set; }
        public DateTime LastActivityTime { get; private set; }
        private bool CalledTimeout { get; set; } = false;

        internal readonly bool isServer = false;
        internal bool IsTerminating { get; set; }
        internal bool IsClosing { get; set; }
        internal TcpListener Owner { get; }

        public TcpSocket()
            : this(new Socket(SocketType.Stream, ProtocolType.Tcp)) { isServer = false; }
        internal TcpSocket(Socket socket, TcpListener owner)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
                throw new SocketException((int)SocketError.ProtocolType);
            Base = socket;
            Owner = owner;
            isServer = true;
            if (socket.Connected) BeginOps();
        }
        public TcpSocket(Socket socket) : this(socket, null) { }

        private void UpdateRemoteAddress()
        {
            if (State == TcpSocketState.Open && !Ended)
            {
                if (Base.RemoteEndPoint is IPEndPoint)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("UpdateRemoteAddress: remote address is IP (is server: {0})", isServer);
#endif
                    RemoteAddress = (Base.RemoteEndPoint as IPEndPoint).Address;
                }
                else if (Base.RemoteEndPoint is DnsEndPoint)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("UpdateRemoteAddress: remote address is hostname (is server: {0})", isServer);
#endif
                    IPAddress[] addresses = Dns.GetHostAddresses((Base.RemoteEndPoint as DnsEndPoint).Host);
#if DEBUG_TCPIO
                    Console.WriteLine("UpdateRemoteAddress: hostname resolved; got address: {0} (is server: {1})", addresses.Length > 0, isServer);
#endif
                    if (addresses.Length == 0) RemoteAddress = null;
                    else RemoteAddress = addresses[0];
                }
            }
            else RemoteAddress = null;
        }

        private void OnConnect(IAsyncResult ar)
        {
#if DEBUG_TCPIO
            Console.WriteLine("OnConnect: closed: {0}", State == TcpSocketState.Closed);
#endif
            if (State == TcpSocketState.Closed)
            {
                Base.Dispose();
                return;
            }
            try
            {
                Base.EndConnect(ar);
            }
            catch (SocketException ex)
            {
#if DEBUG_TCPIO
                Console.WriteLine("OnConnect: unsuccessful; {0}", ex.Message);
#endif
                Control(ex, false, false, false, false);
            }
#if DEBUG_TCPIO
            Console.WriteLine("OnConnect: successful");
#endif
            BeginOps();
        }

        private void BeginOps()
        {
            State = TcpSocketState.Open;
            UpdateRemoteAddress();
            LastActivityTime = DateTime.UtcNow;
#if DEBUG_TCPIO
            Console.WriteLine("FireOpen: enqueue at TcpSocketIOHandler (is server: {0})", isServer);
#endif
            TcpSocketIOHandler.Enqueue(this);
        }
        internal void FireOpen()
        {
#if DEBUG_TCPIO
            Console.WriteLine("FireOpen: fire open (is server: {0})", isServer);
#endif
            if (Owner == null) OnOpen?.Invoke();
            else Owner.FireConnection(this);
        }

        internal bool Control(SocketException ex, bool silent, bool terminate, bool r, bool w)
        {
            if (ex != null || terminate)
            {
#if DEBUG_TCPIO
                Console.WriteLine("Control: terminate (has exception: {0}, silent: {1}, is server: {2})", ex != null, silent, isServer);
#endif
                // terminate & terminate w/ exception
                State = TcpSocketState.Closed;
                if (!silent) Base.Dispose();
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                if (ex != null) OnError?.Invoke(ex);
                OnClose?.Invoke();
                return true;
            }
            else if (r && w)
            {
#if DEBUG_TCPIO
                Console.WriteLine("Control: graceful close (is server: {0})", isServer);
#endif
                // graceful close
                State = TcpSocketState.Closed;
                Base.Close();
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                OnClose?.Invoke();
                return true;
            }
            else if (r)
            {
#if DEBUG_TCPIO
                Console.WriteLine("Control: end readable (is server: {0})", isServer);
#endif
                // end readable
                State = TcpSocketState.Closing;
                Readable.End();
                if (WritableEnded || OnEnd == null)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("Control: OnEnd == null; progress to close (is server: {0})", isServer);
#endif
                    Control(null, false, false, true, true);
                    return true;
                }
                else if (OnEnd != null)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("Control: OnEnd != null (is server: {0})", isServer);
#endif
                    OnEnd?.Invoke();
                    return false;
                }
            }
            else if (w)
            {
                // end writable
#if DEBUG_TCPIO
                Console.WriteLine("Control: end writable (is server: {0})", isServer);
#endif
                State = TcpSocketState.Closing;
                try { Base.Shutdown(SocketShutdown.Send); }
                catch (SocketException ex2) { return Control(ex2, true, false, false, false); }
                Writable.End();
                if (ReadableEnded)
                {
#if DEBUG_TCPIO
                    Console.WriteLine("Control: progress to close (is server: {0})", isServer);
#endif
                    return Control(null, false, false, true, true);
                }
                return false;
            }
            throw new InvalidOperationException("Cannot do anything");
        }
        internal bool FireTimeout()
        {
            if (OnTimeout != null)
            {
                if (CalledTimeout) return false;
                OnTimeout?.Invoke();
            }
            else IOHandler.EnqueueTerminate(this);
            return false;
        }

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);
        public override void Write(byte[] data) => Writable.Write(data);

        internal byte[] ReadOutgoing()
        {
            LastActivityTime = DateTime.UtcNow;
            CalledTimeout = false;
            return Writable.Read(Math.Min(65536, Writable.Buffered));
        }
        internal void WriteIncoming(byte[] data)
        {
            LastActivityTime = DateTime.UtcNow;
            CalledTimeout = false;
            Readable.Write(data);
        }

        public void Terminate()
        {
            switch (State)
            {
                case TcpSocketState.Closed:
                    throw new InvalidOperationException("This socket is closed");
                case TcpSocketState.Opening:
                    //#if DEBUG_TCPIO
                    Console.WriteLine("Control: terminate on opening state (is server: {0})", isServer);
                    //#endif
                    State = TcpSocketState.Closed;
                    break;
                case TcpSocketState.Open:
                case TcpSocketState.Closing:
#if DEBUG_TCPIO
                    Console.WriteLine("Terminate: enqueue terminate (is server: {0})", isServer);
#endif
                    IOHandler.EnqueueTerminate(this);
                    break;
            }
        }
        public override void End()
        {
            switch (State)
            {
                case TcpSocketState.Closed:
                    throw new InvalidOperationException("This socket is closed");
                case TcpSocketState.Opening:
                case TcpSocketState.Open:
#if DEBUG_TCPIO
                    Console.WriteLine("End: enqueue close on open state (is server: {0})", isServer);
#endif
                    IOHandler.EnqueueClose(this);
                    break;
                case TcpSocketState.Closing:
                    if (WritableEnded) throw new InvalidOperationException("This socket is closing; to forcibly close the connection call Terminate() instead");
#if DEBUG_TCPIO
                    Console.WriteLine("End: enqueue close on closing state (is server: {0})", isServer);
#endif
                    IOHandler.EnqueueClose(this);
                    break;
            }
        }

        public void Connect(EndPoint endPoint)
        {
            if (State != TcpSocketState.Closed)
                throw new InvalidOperationException("The socket state is not Closed thus a Connect operation is invalid.");
            State = TcpSocketState.Opening;
            try
            {
                Base.BeginConnect(endPoint, OnConnect, null);
            }
            catch (SocketException ex)
            {
                Control(ex, false, false, false, false);
            }
        }
    }
}
