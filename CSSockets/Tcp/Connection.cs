using System;
using System.Net;
using CSSockets.Streams;
using System.Net.Sockets;

namespace CSSockets.Tcp
{
    public enum TcpSocketState : byte
    {
        Dormant,
        Connecting,
        Open,
        Closing,
        Closed 
    }
    public sealed class Connection : Duplex, IEndable
    {
        private static readonly TcpSocketState[] STATE_TRANSLATE = new TcpSocketState[]
            {
                TcpSocketState.Dormant,
                TcpSocketState.Dormant,
                TcpSocketState.Closed,
                TcpSocketState.Closed,
                TcpSocketState.Closed,
                TcpSocketState.Dormant,
                TcpSocketState.Connecting,
                TcpSocketState.Open,
                TcpSocketState.Open,
                TcpSocketState.Open,
                TcpSocketState.Closing,
                TcpSocketState.Closed
            };

        public SocketWrapper Base { get; private set; }

        public event ControlHandler OnOpen;
        public event ControlHandler OnTimeout;
        public event ControlHandler OnDrain;
        public event ControlHandler OnEnd;
        public event ControlHandler OnClose;
        public event SocketCodeHandler OnError;

        public bool AllowHalfOpen
        {
            get => Base.ClientAllowHalfOpen;
            set => Base.ClientAllowHalfOpen = value;
        }
        public bool Ended => Base.State == WrapperState.Destroyed;
        public ulong BufferedWritable => Writable.Length;
        public TcpSocketState State => STATE_TRANSLATE[(int)Base.State];
        public TimeSpan? TimeoutAfter
        {
            get => Base.ClientTimeoutAfter;
            set => Base.ClientTimeoutAfter = value;
        }
        public IPAddress LocalAddress => Base.Local?.Address;
        public IPAddress RemoteAddress => Base.Remote?.Address;

        internal Connection(SocketWrapper wrapper)
        {
            Base = wrapper;
            Base.ClientOnConnect = _OnOpen;
            Base.ClientOnTimeout = _OnTimeout;
            Base.ClientOnShutdown = _OnRemoteShutdown;
            Base.ClientOnClose = _OnClose;
            Base.WrapperOnSocketError = _OnError;
        }
        public Connection() : this(new SocketWrapper())
        {
            Base.WrapperBind();
            Base.WrapperAddClient(this);
        }

        internal readonly PrimitiveBuffer Writable = new PrimitiveBuffer();
        private void _OnOpen() => OnOpen?.Invoke();
        private void _OnTimeout()
        {
            lock (Sync)
            {
                if (OnTimeout != null) OnTimeout();
                else if (!End()) Terminate();
            }
        }
        private void _OnClose() => OnClose?.Invoke();
        private void _OnRemoteShutdown() => OnEnd?.Invoke();
        private void _OnError(SocketError error)
        {
            lock (Sync)
            {
                OnError?.Invoke(error);
                Terminate();
            }
        }

        internal bool  _ReadableWrite(byte[] source) => HandleReadable(source);
        internal byte[] _WritableRead(ulong maximum)
        {
            lock (Sync)
            {
                ulong length = Math.Min(maximum, BufferedWritable);
                if (length == BufferedWritable) OnDrain?.Invoke();
                return Writable.Read(length);
            }
        }

        public bool CanRead => Base.State == WrapperState.ClientOpen || Base.State == WrapperState.ClientReadonly;
        public override byte[] Read() => Readable.Read(BufferedReadable);
        public override byte[] Read(ulong length) => Readable.Read(length);

        public bool CanWrite => Base.State == WrapperState.ClientOpen || Base.State == WrapperState.ClientWriteonly;
        protected override bool HandleWritable(byte[] data) => Writable.Write(data);
        public override bool Write(byte[] source)
        {
            lock (Sync)
            {
                if (!CanWrite) return false;
                return base.Write(source);
            }
        }

        public bool End()
        {
            lock (Sync)
            {
                switch (Base.State)
                {
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
            lock (Sync)
            {
                if (State == TcpSocketState.Closed) return false;
                Base.ClientTerminate();
                return true;
            }
        }

        public bool Connect(EndPoint endPoint)
        {
            lock (Sync)
            {
                if (Base.State > WrapperState.ClientDormant) return false;
                Base.ClientConnect(endPoint);
                return true;
            }
        }
    }
}
