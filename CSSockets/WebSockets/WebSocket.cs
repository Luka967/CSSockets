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
    abstract public class WebSocket : ICorkable, IPausable
    {
        static int i = 0;
        int id = ++i;
        public TcpSocket Base { get; set; }
        public TcpSocketState State => Base.State;
        public bool Paused => Base.Paused;
        public bool Corked => Base.Corked;
        protected void ThrowIfNotOpen()
        {
            if (State != TcpSocketState.Open) throw new InvalidOperationException("Cannot perform this operation as the socket is either disconnecting or not connected");
        }
        public RequestHead RequestHead { get; }
        public IPAddress RemoteAddress => Base.RemoteAddress;
        protected bool FiredClose { get; private set; }

        public event BinaryMessageHandler OnBinary;
        public event StringMessageHandler OnString;
        public event BinaryMessageHandler OnPing;
        public event BinaryMessageHandler OnPong;
        public event CloseMessageHandler OnClose;

        protected FrameParser FrameParser { get; }
        protected FrameMerger FrameMerger { get; }

        protected WebSocket(TcpSocket socket, RequestHead head, byte[] trail)
        {
            Base = socket;
            Base.OnClose += OnSurpriseEnd;
            Base.OnClose += OnSocketEnd;
            RequestHead = head;
            FrameParser = new FrameParser();
            FrameMerger = new FrameMerger();
            FrameParser.OnOutput += OnIncomingFrame;
            FrameMerger.OnOutput += OnIncomingMessage;
            FrameParser.Write(trail);
            Base.Pipe(FrameParser);
        }

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
            if (State == TcpSocketState.Closed) return;
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
                    OnClose?.Invoke(code, reason);
                    AnswerClose(code, reason);
                    break;
                case 9: OnPing?.Invoke(message.Data); AnswerPing(message.Data); break;
                case 10: OnPong?.Invoke(message.Data); break;
                default: ForciblyClose(); break;
            }
        }

        virtual public void Cork() => Base.Cork();
        virtual public void Uncork() => Base.Uncork();
        virtual public void Pause() => Base.Pause();
        virtual public void Resume() => Base.Resume();

        private void FireClose(ushort code, string reason)
        {
            if (FiredClose) return;
            FiredClose = true;
            OnClose?.Invoke(code, reason);
        }
        private void OnSurpriseEnd() => FireClose(0, null);
        private void OnSocketEnd()
        {
            FrameParser.End();
            FrameMerger.End();
        }
        protected void InitiateClose(ushort code, string reason)
        {
            if (FiredClose) return;
            FiredClose = true;
            Base.OnClose -= OnSurpriseEnd;
            Base.Pause();
            Base.End();
            FireClose(code, reason);
        }
        protected void ForciblyClose()
        {
            if (FiredClose) return;
            FiredClose = true;
            Base.OnClose -= OnSurpriseEnd;
            Base.Pause();
            Base.Terminate();
            FireClose(0, null);
        }
        protected void Send(Frame frame)
        {
            ThrowIfNotOpen();
            Base.Write(frame.Serialize());
        }
        abstract public void Send(byte[] data);
        abstract public void Send(string data);
        abstract public void Ping(byte[] data = null);
        abstract public void Close(ushort code, string reason);

        abstract protected void AnswerPing(byte[] data);
        abstract protected void AnswerClose(ushort code, string reason);
    }
}
