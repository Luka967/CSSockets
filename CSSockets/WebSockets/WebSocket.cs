using System;
using System.Net;
using System.Text;
using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Reference;

namespace CSSockets.WebSockets
{
    public delegate void BinaryMessageHandler(byte[] data);
    public delegate void StringMessageHandler(string data);
    public delegate void CloseMessageHandler(ushort code, string reason);
    abstract public class WebSocket : IPausable, ICorkable
    {
        public TcpSocket Base { get; set; }
        public TcpSocketState State => Base.State;
        protected void ThrowIfNotOpen()
        {
            if (State != TcpSocketState.Open) throw new InvalidOperationException("Cannot perform this operation as the socket is either disconnecting or not connected");
        }
        public RequestHead RequestHead { get; }
        public IPAddress RemoteAddress => Base.RemoteAddress;
        public bool IsPaused => Base.IsPaused;
        public bool IsCorked => Base.IsCorked;
        public ulong BufferedReadable => Base.BufferedReadable;
        public ulong BufferedWritable => Base.BufferedReadable;

        public event BinaryMessageHandler OnBinary;
        public event StringMessageHandler OnString;
        public event BinaryMessageHandler OnPing;
        public event BinaryMessageHandler OnPong;
        public event CloseMessageHandler OnClose;

        protected bool IsClosing { get; private set; } = false;
        protected object OpsLock { get; } = new object();
        protected FrameParser FrameParser { get; } = new FrameParser();
        protected FrameMerger FrameMerger { get; } = new FrameMerger();

        protected WebSocket(TcpSocket socket, RequestHead head)
        {
            Base = socket;
            Base.OnClose += OnSurpriseEnd;
            Base.OnClose += OnSocketEnd;
            RequestHead = head;
            FrameParser.OnOutput += OnIncomingFrame;
            FrameMerger.OnOutput += OnIncomingMessage;
        }
        internal void WriteTrail(byte[] trail) => FrameParser.Write(trail);

        abstract protected bool IsValidFrame(Frame frame);

        private void OnIncomingFrame(Frame frame)
        {
            if (!IsValidFrame(frame))
            {
                ForciblyClose();
                return;
            }
            FrameMergeResponse res = FrameMerger.MergeFrame(frame);
            switch (res)
            {
                case FrameMergeResponse.ContinuationOnNoOpcode:
                case FrameMergeResponse.OpcodeOnNonFin:
                    ForciblyClose();
                    break;
                case FrameMergeResponse.Valid: break;
            }
        }

        private void OnIncomingMessage(Message message)
        {
            lock (OpsLock)
            {
                if (IsClosing) return;
                switch (message.Opcode)
                {
                    case 1:
                        string str;
                        try { str = Encoding.UTF8.GetString(message.Data); }
                        catch (ArgumentException) { ForciblyClose(); return; }
                        OnString?.Invoke(str);
                        break;
                    case 2: OnBinary?.Invoke(message.Data); break;
                    case 8:
                        ushort code = (ushort)(message.Data.Length == 0 ? 0 : message.Data[0] * 256u + message.Data[1]);
                        string reason = message.Data.Length >= 2 ? Encoding.UTF8.GetString(message.Data, 2, message.Data.Length - 2) : null;
                        AnswerClose(code, reason);
                        break;
                    case 9: OnPing?.Invoke(message.Data); AnswerPing(message.Data); break;
                    case 10: OnPong?.Invoke(message.Data); break;
                    default: ForciblyClose(); break;
                }
            }
        }

        virtual public bool Cork() => Base.Pause();
        virtual public bool Uncork() => Base.Resume();
        virtual public bool Pause() => Base.Pause();
        virtual public bool Resume() => Base.Resume();

        private void FireClose(ushort code, string reason) => OnClose?.Invoke(code, reason);
        private void OnSurpriseEnd() => FireClose(0, null);
        private void OnSocketEnd()
        {
            FrameParser.End();
            FrameMerger.End();
        }
        protected void InitiateClose(ushort code, string reason)
        {
            Base.OnClose -= OnSurpriseEnd;
            Base.End();
            FireClose(code, reason);
            IsClosing = true;
        }
        protected void ForciblyClose()
        {
            Base.OnClose -= OnSurpriseEnd;
            Base.Terminate();
            FireClose(0, null);
            IsClosing = true;
        }
        protected void Send(Frame frame)
        {
            ThrowIfNotOpen();
            if (IsClosing) return;
            frame.Serialize(Base);
        }
        abstract public void Send(byte[] data);
        abstract public void Send(string data);
        abstract public void Ping(byte[] data = null);
        abstract public void Close(ushort code, string reason);

        abstract protected void AnswerPing(byte[] data);
        abstract protected void AnswerClose(ushort code, string reason);
    }
}
