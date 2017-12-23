using System;
using System.Text;
using CSSockets.Tcp;
using CSSockets.Streams;

namespace CSSockets.WebSockets
{
    public delegate void BinaryMessageHandler(byte[] data);
    public delegate void StringMessageHandler(string data);
    public delegate void CloseMessageHandler(ushort code, string reason);
    abstract public class WebSocket : ICorkable, IPausable
    {
        public TcpSocket Base { get; set; }
        public TcpSocketState State => Base.State;
        public bool Paused => Base.Paused;
        public bool Corked => Base.Corked;
        protected void ThrowIfNotOpen()
        {
            if (State != TcpSocketState.Open) throw new InvalidOperationException("Cannot perform this operation as the socket is either disconnecting or not connected");
        }

        public event BinaryMessageHandler OnBinary;
        public event StringMessageHandler OnString;
        public event BinaryMessageHandler OnPing;
        public event BinaryMessageHandler OnPong;
        public event CloseMessageHandler OnClose;

        protected FrameParser FrameParser { get; }
        protected FrameMerger FrameMerger { get; }

        protected WebSocket(TcpSocket socket, byte[] trail)
        {
            Base = socket;
            FrameParser = new FrameParser();
            FrameMerger = new FrameMerger();
            FrameParser.OnOutput += OnIncomingFrame;
            FrameMerger.OnOutput += OnIncomingMessage;
            FrameParser.Write(trail);
            Base.Pipe(FrameParser);
        }

        private void OnIncomingFrame(Frame frame)
        {
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
            switch (message.Opcode)
            {
                case 1: OnString?.Invoke(Encoding.UTF8.GetString(message.Data)); break;
                case 2: OnBinary?.Invoke(message.Data); break;
                case 8:
                    ushort code = (ushort)(message.Data.Length == 0 ? 0 : message.Data[0] * 256u + message.Data[1]);
                    string reason = Encoding.UTF8.GetString(message.Data, 2, message.Data.Length - 2);
                    OnClose?.Invoke(code, reason);
                    AnswerClose(code, reason);
                    break;
                case 9: OnPing?.Invoke(message.Data); AnswerPing(message.Data); break;
                case 10: OnPong?.Invoke(message.Data); break;
            }
        }

        virtual public void Cork()
        {
            ThrowIfNotOpen();
            Base.Cork();
        }

        virtual public void Uncork()
        {
            ThrowIfNotOpen();
            Base.Uncork();
        }

        virtual public void Pause()
        {
            ThrowIfNotOpen();
            Base.Pause();
        }

        virtual public void Resume()
        {
            ThrowIfNotOpen();
            Base.Resume();
        }

        protected void InitiateClose() => Base.End();
        protected void ForciblyClose()
        {
            Base.Terminate();
            OnClose?.Invoke(0, null);
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
