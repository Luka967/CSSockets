using System;
using System.Net;
using System.Threading;
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
        public TcpSocketState State { get; internal set; }
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

        public TcpSocket()
            : this(new Socket(SocketType.Stream, ProtocolType.Tcp)) { }
        public TcpSocket(Socket socket)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
                throw new SocketException((int)SocketError.ProtocolType);
            Base = socket;
            if (Base.Connected) BeginOps();
            else State = TcpSocketState.Closed;
        }

        private void UpdateRemoteAddress()
        {
            if (State == TcpSocketState.Open)
            {
                if (Base.RemoteEndPoint is IPEndPoint)
                    RemoteAddress = (Base.RemoteEndPoint as IPEndPoint).Address;
                else if (Base.RemoteEndPoint is DnsEndPoint)
                {
                    IPAddress[] addresses = Dns.GetHostAddresses((Base.RemoteEndPoint as DnsEndPoint).Host);
                    if (addresses.Length == 0) RemoteAddress = null;
                    else RemoteAddress = addresses[0];
                }
            }
            else RemoteAddress = null;
        }

        private void OnConnect(IAsyncResult ar)
        {
            if (Ended)
            {
                State = TcpSocketState.Closed;
                Base.Dispose();
                return;
            }
            try
            {
                Base.EndConnect(ar);
            }
            catch (SocketException e) { return; }
            BeginOps();
        }

        private void BeginOps()
        {
            State = TcpSocketState.Open;
            UpdateRemoteAddress();
            LastActivityTime = DateTime.UtcNow;
            TcpSocketIOHandler.Enqueue(this);
            OnOpen?.Invoke();
        }

        internal bool SocketControl(SocketException ex, bool silentClose, bool terminate, bool close, bool r, bool w)
        {
            if (ex != null)
            {
                State = TcpSocketState.Closed;
                if (!silentClose) try { Base.Dispose(); } catch (ObjectDisposedException) { }
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                OnError?.Invoke(ex);
                OnClose?.Invoke();
                return true;
            }
            else if (silentClose)
            {
                State = TcpSocketState.Closed;
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                OnClose?.Invoke();
                return true;
            }
            else if (terminate)
            {
                State = TcpSocketState.Closed;
                Base.Dispose();
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                OnClose?.Invoke();
                return true;
            }
            else if (close)
            {
                State = TcpSocketState.Closed;
                Base.Close();
                if (!ReadableEnded) Readable.End();
                if (!WritableEnded) Writable.End();
                OnClose?.Invoke();
                return true;
            }
            else
            {
                bool alreadyClosed = false;
                if (r)
                {
                    Base.Shutdown(SocketShutdown.Receive);
                    if (!ReadableEnded) Readable.End();
                    alreadyClosed = ProgressClose();
                }
                if (w && !alreadyClosed)
                {
                    Base.Shutdown(SocketShutdown.Send);
                    if (!WritableEnded) Writable.End();
                    alreadyClosed = ProgressClose();
                }
                return alreadyClosed;
            }
        }
        internal bool ProgressClose()
        {
            if (State == TcpSocketState.Open)
            {
                State = TcpSocketState.Closing;
                if (OnEnd != null) OnEnd();
                else if (!WritableEnded && Writable.Buffered == 0)
                {
                    SocketControl(null, false, false, false, false, true);
                    return true;
                }
            }
            else if (State == TcpSocketState.Closing)
            {
                State = TcpSocketState.Closed;
                SocketControl(null, false, false, true, false, false);
                return true;
            }
            return false;
        }
        internal void FireTimeout()
        {
            if (OnTimeout != null && !CalledTimeout)
            {
                OnTimeout();
                CalledTimeout = true;
            }
            else if (OnTimeout == null)
                SocketControl(null, false, false, false, false, false);
        }
        public void ResetTimeout() => LastActivityTime = DateTime.UtcNow;

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);
        public override void Write(byte[] data) => Writable.Write(data);

        internal byte[] ReadOutgoing()
        {
            LastActivityTime = DateTime.UtcNow;
            CalledTimeout = false;
            return Writable.Read();
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
                    throw new InvalidOperationException("This socket is opening; to prematurely close the connection call End() instead");
                case TcpSocketState.Open:
                case TcpSocketState.Closing:
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
                    IOHandler.EnqueueCloseProgress(this);
                    break;
                case TcpSocketState.Closing:
                    if (WritableEnded)
                        throw new InvalidOperationException("This socket is closing; to forcibly close the connection call Terminate() instead");
                    IOHandler.EnqueueCloseProgress(this);
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
                SocketControl(ex, true, false, false, false, false);
            }
        }
    }
}
