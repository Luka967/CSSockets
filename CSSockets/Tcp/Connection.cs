using System;
using System.Net;
using CSSockets.Streams;
using System.Net.Sockets;
using CSSockets.Tcp.Wrap;

namespace CSSockets.Tcp
{
    public enum TcpSocketState : byte
    {
        Dormant = 0,
        Connecting = 1,
        Open = 2,
        Closing = 3,
        Closed = 4
    }
    public sealed class Connection : BaseDuplex
    {
        private static readonly TcpSocketState[] STATES =
            new TcpSocketState[]
            {
                TcpSocketState.Dormant,     // SocketWrapperState.ClientDormant
                TcpSocketState.Connecting,  // SocketWrapperState.ClientConnecting
                TcpSocketState.Open,        // SocketWrapperState.ClientOpen
                TcpSocketState.Open,        // SocketWrapperState.ClientReadonly
                TcpSocketState.Open,        // SocketWrapperState.ClientWriteonly
                TcpSocketState.Closing,     // SocketWrapperState.ClientLastWrite
                TcpSocketState.Closed,      // SocketWrapperState.ClientClosed
                TcpSocketState.Closed       // SocketWrapperState.Destroyed
            };

        public event ControlHandler OnOpen;
        public event ControlHandler OnTimeout;
        public override event ControlHandler OnEnd;
        public event ControlHandler OnClose;
        public event SocketErrorHandler OnError;
        public TcpSocketState State => STATES[(int)Base.State - 5];
        public bool AllowHalfOpen
        {
            get => Base.ClientAllowHalfOpen;
            set => Base.ClientAllowHalfOpen = value;
        }
        public TimeSpan? TimeoutAfter
        {
            get => Base.ClientTimeoutAfter;
            set => Base.ClientTimeoutAfter = value;
        }
        public IPAddress LocalAddress => Base.Local?.Address;
        public IPAddress RemoteAddress => Base.Remote?.Address;

        public SocketWrapper Base { get; private set; }
        internal bool isComingFromServer = false;

        internal Connection(SocketWrapper wrapper)
        {
            Base = wrapper;
            Base.ClientOnConnect = _OnOpen;
            Base.ClientOnTimeout = _OnTimeout;
            Base.ClientOnRecvShutdown = _OnRemoteShutdown;
            Base.ClientOnClose = _OnClose;
            Base.WrapperOnSocketError = _OnError;
        }
        public Connection(EndPoint endPoint) : this(new SocketWrapper())
        {
            Base.WrapperBind();
            Base.WrapperAddClient(this);
            Base.ClientConnect(endPoint);
        }

        private void _OnOpen() => OnOpen?.Invoke();
        private void _OnTimeout()
        {
            if (OnTimeout != null) OnTimeout();
            else if (!End()) Terminate();
        }
        private void _OnClose() => OnClose?.Invoke();
        private void _OnRemoteShutdown() => OnEnd?.Invoke();
        private void _OnError(SocketError error)
        {
            OnError?.Invoke(error);
            Terminate();
        }

        internal bool _WriteReadable(byte[] source) => Readable.Write(source);
        internal byte[] _ReadWritable(ulong length) => Writable.Read(Math.Min(Writable.Buffered, length));
        internal bool _EndReadable() => EndReadable();
        internal bool _EndWritable() => EndWritable();

        public bool CanRead => isComingFromServer || Base.State == WrapperState.ClientOpen || Base.State == WrapperState.ClientReadonly;
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(ulong length) => Readable.Read(length);
        public override ulong Read(byte[] destination) => Readable.Read(destination);

        public bool CanWrite => isComingFromServer || Base.State == WrapperState.ClientOpen || Base.State == WrapperState.ClientWriteonly;
        public override bool Write(byte[] source)
        {
            if (!CanWrite) throw new InvalidOperationException("Cannot write to socket");
            return Writable.Write(source);
        }
        public override bool Write(byte[] source, ulong start, ulong end)
        {
            if (!CanWrite) throw new InvalidOperationException("Cannot write to socket");
            return Writable.Write(source, start, end);
        }

        public override bool End()
        {
            lock (EndLock)
            {
                if (Ended) return false;
                switch (Base.State)
                {
                    case WrapperState.Unset:
                        if (isComingFromServer)
                            Base.ClientShutdown();
                        break;
                    case WrapperState.ClientOpen:
                    case WrapperState.ClientWriteonly:
                        Base.ClientShutdown();
                        break;
                    case WrapperState.ClientDormant:
                    case WrapperState.ClientConnecting:
                        Base.ClientTerminate();
                        break;
                    default: return false;
                }
                return true;
            }
        }

        public bool Terminate()
        {
            lock (EndLock)
            {
                if (State == TcpSocketState.Closed) return false;
                Base.ClientTerminate();
                return true;
            }
        }
    }
}
