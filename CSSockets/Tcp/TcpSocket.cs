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
        public TcpSocketState State { get; private set; }
        public bool AllowHalfOpen { get; set; }
        public Socket Base { get; }

        public event TcpSocketControlHandler OnOpen;
        public event TcpExceptionHandler OnError;
        public event TcpSocketControlHandler OnEnd;
        public event TcpSocketControlHandler OnClose;

        public TcpSocket(bool allowHalfOpen = false)
            : this(new Socket(SocketType.Stream, ProtocolType.Tcp), allowHalfOpen) { }
        public TcpSocket(Socket socket, bool allowHalfOpen = false)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
                throw new SocketException((int)SocketError.ProtocolType);
            AllowHalfOpen = allowHalfOpen;
            Base = socket;
            if (Base.Connected) BeginOps();
            else State = TcpSocketState.Closed;
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
            catch (SocketException e)
            {
                EndWithError(e.SocketErrorCode);
                return;
            }
            BeginOps();
        }

        private void BeginOps()
        {
            State = TcpSocketState.Open;
            ThreadPool.QueueUserWorkItem(RecvLoop);
            ThreadPool.QueueUserWorkItem(SendLoop);
            OnOpen?.Invoke();
        }

        private void RecvLoop(object _)
        {
            int bufSize = Base.ReceiveBufferSize;
            byte[] buf = new byte[bufSize];
            while (true)
            {
                int len = Base.Receive(buf, 0, bufSize, SocketFlags.None, out SocketError code);
                if (code == SocketError.Interrupted || len == 0)
                    break;
                else if (code != SocketError.Success)
                {
                    EndWithError(code);
                    return;
                }
                Readable.Write(buf, 0, len);
            }
            if (!Readable.Ended)
                EndReadable();
        }
        private void SendLoop(object _)
        {
            while (true)
            {
                if (Writable.Ended || (State == TcpSocketState.Closing && Writable.Buffered == 0))
                    break;
                byte[] next = Writable.Read();
                if (next == null)
                    break;
                Base.Send(next, 0, next.Length, SocketFlags.None, out SocketError code);
                if (code == SocketError.Interrupted)
                    break;
                else if (code != SocketError.Success)
                {
                    EndWithError(code);
                    return;
                }
            }
            if (!Writable.Ended)
                EndWritable();
        }

        private void SetClosing()
        {
            if (State == TcpSocketState.Closing)
                return;
            if (AllowHalfOpen) OnEnd?.Invoke();
            else
            {
                State = TcpSocketState.Closing;
                if (Writable.Buffered == 0)
                    EndWritable();
            }
        }
        protected override void OnEnded()
        {
            State = TcpSocketState.Closed;
            Base.Close();
            OnClose?.Invoke();
        }
        protected override void EndReadable()
        {
            SetClosing();
            Base.Shutdown(SocketShutdown.Receive);
            base.EndReadable();
        }
        protected override void EndWritable()
        {
            Base.Shutdown(SocketShutdown.Send);
            base.EndWritable();
        }
        private void EndWithError(SocketError error)
        {
            OnError?.Invoke(new SocketException((int)error));
            OnClose?.Invoke();
            EndReadable();
            EndWritable();
        }
        public override void End()
        {
            switch (State)
            {
                case TcpSocketState.Closed:
                case TcpSocketState.Opening:
                    base.End();
                    break;
                case TcpSocketState.Open:
                    AllowHalfOpen = false;
                    SetClosing();
                    break;
                case TcpSocketState.Closing:
                    throw new SocketException((int)SocketError.Disconnecting);
            }
        }

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);
        public override void Write(byte[] data) => Writable.Write(data);
        public void Connect(EndPoint endPoint)
        {
            if (State != TcpSocketState.Closed)
                throw new InvalidOperationException("The socket state is not Closed thus a Connect operation is invalid.");
            State = TcpSocketState.Opening;
            Base.BeginConnect(endPoint, OnConnect, null);
        }
    }
}
