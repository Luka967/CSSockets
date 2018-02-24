using System;
using System.Net;
using CSSockets.Streams;
using System.Net.Sockets;

namespace CSSockets.Tcp
{
    public delegate void TcpSocketErrorHandler(SocketError socketError);
    public enum TcpSocketState : byte
    {
        /// <summary>
        /// No operations are possible and the socket is not doing a connect operation in the background.
        /// </summary>
        Dormant = 0,
        /// <summary>
        /// No operations are possible while the socket is trying to connect to a remote endpoint.
        /// </summary>
        Opening = 1,
        /// <summary>
        /// All operations are possible and data transferring is guaranteed.
        /// </summary>
        Open = 2,
        /// <summary>
        /// No operations are possible because one of the endpoints has sent a FIN frame.
        /// </summary>
        Closing = 3,
        /// <summary>
        /// No operations are possible and the socket is disposed.
        /// </summary>
        Closed = 4
    }
    internal enum TcpSocketOp : byte
    {
        Noop = 0,
        Open = 1,
        EndRead = 2,
        EndWrite = 3,
        Close = 4,
        Terminate = 5,
        Throw = 6
    }

    public sealed class TcpSocket : BaseDuplex
    {
        public Socket Base { get; } = null;
        public IPAddress RemoteAddress { get; private set; } = null;
        public TcpSocketState State { get; private set; } = TcpSocketState.Dormant;

        internal SocketIOHandler.IOThread Handle = null;
        internal TcpListener Creator = null;
        internal bool HfiredTimeout = false;
        internal DateTime HlastActivity;
        internal bool HqueuedOpen = false;
        internal bool HqueuedEndW = false;
        internal bool HqueuedClose = false;
        internal bool HqueuedTerm = false;

        public event TcpSocketErrorHandler OnError;
        public event ControlHandler OnTimeout;
        public event ControlHandler OnOpen;
        public event ControlHandler OnClose;

        public bool CanTimeout { get; set; } = false;
        public TimeSpan TimeoutAfter { get; set; } = new TimeSpan();

        public TcpSocket() => Base = new Socket(SocketType.Stream, ProtocolType.Tcp);
        internal TcpSocket(Socket socket)
        {
            if (socket.SocketType != SocketType.Stream)
                throw new ArgumentException("Provided socket is not using TCP");
            Base = socket;
        }

        private void OnConnectEnd(IAsyncResult ar)
        {
            try { Base.EndConnect(ar); }
            catch (SocketException ex) { Control(TcpSocketOp.Throw, ex.SocketErrorCode); return; }
            LinkToIOHandler();
        }
        internal void LinkToIOHandler()
        {
            Handle = SocketIOHandler.GetBest();
            Handle.Open(this);
        }
        internal bool FireTimeout()
        {
            if (HfiredTimeout) return false;
            HfiredTimeout = true;
            if (OnTimeout != null) { OnTimeout?.Invoke(); return false; }
            return true;
        }
        internal void UpdateRemoteAddress(EndPoint endPoint)
        {
            if (endPoint is IPEndPoint) RemoteAddress = (endPoint as IPEndPoint).Address;
            else try
                {
                    IPHostEntry resolved = Dns.GetHostEntry((endPoint as DnsEndPoint).Host);
                    if (resolved.AddressList.Length > 0) RemoteAddress = resolved.AddressList[0];
                }
                catch (SocketException) { }
            if (RemoteAddress.IsIPv4MappedToIPv6)
                RemoteAddress = RemoteAddress.MapToIPv4();
        }
        internal bool Control(TcpSocketOp op, SocketError error = SocketError.Success)
        {
            HlastActivity = DateTime.UtcNow;
            switch (op)
            {
                case TcpSocketOp.Throw:
                    if (State != TcpSocketState.Closed) Base.Dispose();
                    State = TcpSocketState.Closed;
                    if (!ReadableEnded) EndReadable();
                    if (!WritableEnded) EndWritable();
                    OnError?.Invoke(error);
                    OnClose?.Invoke();
                    return true;
                case TcpSocketOp.Terminate:
                    if (State == TcpSocketState.Closed) return false;
                    Base.Dispose();
                    State = TcpSocketState.Closed;
                    if (!ReadableEnded) EndReadable();
                    if (!WritableEnded) EndWritable();
                    OnClose?.Invoke();
                    return true;
                case TcpSocketOp.Close:
                    if (State == TcpSocketState.Closed) return false;
                    Base.Close();
                    State = TcpSocketState.Closed;
                    if (!ReadableEnded) EndReadable();
                    if (!WritableEnded) EndWritable();
                    OnClose?.Invoke();
                    return true;
                case TcpSocketOp.EndWrite:
                    if (WritableEnded) return false;
                    State = TcpSocketState.Closing;
                    Base.Shutdown(SocketShutdown.Send);
                    EndWritable();
                    if (ReadableEnded) return Control(TcpSocketOp.Close);
                    return false;
                case TcpSocketOp.EndRead:
                    if (ReadableEnded) return false;
                    State = TcpSocketState.Closing;
                    Base.Shutdown(SocketShutdown.Receive);
                    EndReadable();
                    if (WritableEnded) return Control(TcpSocketOp.Close);
                    if (EndHasListeners) FireEnd();
                    else return Control(TcpSocketOp.EndWrite);
                    return false;
                case TcpSocketOp.Open:
                    State = TcpSocketState.Open;
                    UpdateRemoteAddress(Base.RemoteEndPoint);
                    if (Creator == null) OnOpen?.Invoke();
                    else Creator.FireConnection(this);
                    return false;
                default: return false;
            }
        }

        public void CheckRead()
        {
            if (State == TcpSocketState.Open || (State == TcpSocketState.Closing && !ReadableEnded)) return;
            throw new SocketException((int)SocketError.NotConnected);
        }
        public bool CheckWrite() => State == TcpSocketState.Open;
        public override byte[] Read()
        {
            CheckRead();
            return Readable.Read();
        }
        public override byte[] Read(ulong length)
        {
            CheckRead();
            return Readable.Read(length);
        }
        public override ulong Read(byte[] destination)
        {
            CheckRead();
            return Readable.Read(destination);
        }
        public override bool Write(byte[] source) => CheckWrite() && Writable.Write(source);
        public override bool Write(byte[] source, ulong start, ulong end) => CheckWrite() && Writable.Write(source, start, end);

        public bool Connect(EndPoint endPoint)
        {
            if (State != TcpSocketState.Dormant) return false;
            try { Base.BeginConnect(endPoint, OnConnectEnd, null); }
            catch (SocketException ex) { Control(TcpSocketOp.Throw, ex.SocketErrorCode); return false; }
            return true;
        }

        internal bool WriteReadable(byte[] source)
        {
            HlastActivity = DateTime.UtcNow;
            return Readable.Write(source);
        }
        internal byte[] ReadWritable()
        {
            HlastActivity = DateTime.UtcNow;
            return Writable.Read(Math.Min(Writable.Buffered, 65536));
        }

        protected override bool EndReadable()
        {
            lock (EndLock) return Readable.End();
        }
        protected override bool EndWritable()
        {
            lock (EndLock) return Writable.End();
        }
        public override bool End()
        {
            lock (EndLock)
            {
                switch (State)
                {
                    case TcpSocketState.Dormant:
                        Base.Dispose();
                        State = TcpSocketState.Closed;
                        return true;
                    case TcpSocketState.Open:
                        return Handle.EndWrite(this);
                    case TcpSocketState.Closing:
                        return Handle.Close(this);
                    default: return false;
                }
            }
        }
        public bool Terminate()
        {
            lock (EndLock)
            {
                switch (State)
                {
                    case TcpSocketState.Opening:
                    case TcpSocketState.Open:
                    case TcpSocketState.Closing:
                        return Handle.Terminate(this);
                    default: return false;
                }
            }
        }
    }
}
