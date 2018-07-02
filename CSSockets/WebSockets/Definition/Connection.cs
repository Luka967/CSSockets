using System.Text;
using CSSockets.Tcp;
using CSSockets.Streams;

namespace CSSockets.WebSockets.Definition
{
    public delegate void BinaryMessageHandler(byte[] data);
    public delegate void StringMessageHandler(string data);
    public delegate void CloseMessageHandler(ushort code, string reason);
    public abstract partial class Connection
    {
        public Tcp.Connection Base { get; }
        public IMode Mode { get; }
        public bool Opening => Base.State == TcpSocketState.Connecting;
        public bool Open => Base.State == TcpSocketState.Open;
        public bool Closing => SentClose;
        public bool Closed => RecvClose;

        public virtual event BinaryMessageHandler OnBinary;
        public virtual event StringMessageHandler OnString;
        public virtual event BinaryMessageHandler OnPing;
        public virtual event BinaryMessageHandler OnPong;
        public virtual event CloseMessageHandler OnClose;

        public Connection(Tcp.Connection connection, IMode mode)
        {
            Mode = mode;
            Base = connection;
            Base.OnClose += FinishClose;
            Parser.OnCollect += OnParserCollect;
            Merger.OnCollect += OnMergerCollect;
        }
        public bool WriteTrail(byte[] trail)
        {
            if (!Parser.Write(trail)) return false;
            return Base.Pipe(Parser);
        }

        protected readonly FrameParser Parser = new FrameParser();
        protected readonly FrameMerger Merger = new FrameMerger();

        protected readonly object Sync = new object();
        protected ushort CloseCode = 1006;
        protected string CloseReason = null;
        protected bool RecvClose = false;
        protected bool SentClose = false;
        protected bool StartClose(ushort code, string reason)
        {
            lock (Sync)
            {
                if (RecvClose || SentClose) return false;
                CloseCode = code;
                CloseReason = reason;
                return SentClose = true;
            }
        }
        protected void FinishClose()
        {
            lock (Sync)
            {
                RecvClose = true;
                OnClose?.Invoke(CloseCode = 1006, CloseReason = "");
                Base.OnClose -= FinishClose;
            }
        }
        protected bool FinishClose(ushort code)
        {
            lock (Sync)
            {
                if (!SentClose || RecvClose) return false;
                if (CloseCode != code) return false;
                OnClose?.Invoke(CloseCode, CloseReason);
                Base.OnClose -= FinishClose;
                return RecvClose = true;
            }
        }
        protected bool FinishClose(ushort code, string reason)
        {
            lock (Sync)
            {
                if (RecvClose) return false;
                OnClose?.Invoke(CloseCode = code, CloseReason = reason);
                Base.OnClose -= FinishClose;
                return RecvClose = true;
            }
        }

        protected abstract void OnParserCollect(Frame frame);
        protected abstract void OnMergerCollect(Message message);
        protected bool HandleMessage(Message message)
        {
            lock (Sync) switch (message.Opcode)
                {
                    case 1: OnString?.Invoke(Encoding.UTF8.GetString(message.Payload)); return true;
                    case 2: OnBinary?.Invoke(message.Payload); return true;
                    case 8:
                        if (message.Length < 2)
                        {
                            FinishClose();
                            return true;
                        }
                        ushort code = (ushort)(message.Payload[0] * 256 + message.Payload[1]);
                        if (SentClose)
                        {
                            if (message.Length > 2) return false;
                            return FinishClose(code);
                        }
                        string reason = Encoding.UTF8.GetString(PrimitiveBuffer.Slice(message.Payload, 2, message.Length));
                        return FinishClose(code, reason);
                    case 9: OnPing?.Invoke(message.Payload); return true;
                    case 10: OnPong?.Invoke(message.Payload); return true;
                    default: return false;
                }
        }

        protected bool Send(Frame frame)
        {
            lock (Sync)
            {
                if (SentClose || RecvClose || !Base.CanWrite) return false;
                frame.SerializeTo(Base);
                return true;
            }
        }
        public abstract bool SendBinary(byte[] payload);
        public virtual bool SendBinary(byte[] payload, ushort start) => SendBinary(PrimitiveBuffer.Slice(payload, start, (ulong)payload.LongLength));
        public virtual bool SendBinary(byte[] payload, ushort start, ulong end) => SendBinary(PrimitiveBuffer.Slice(payload, start, end));
        public abstract bool SendString(string payload);
        public abstract bool SendPing(byte[] payload = null);
        public virtual bool SendPing(byte[] payload, ushort start) => SendPing(PrimitiveBuffer.Slice(payload, start, (ulong)payload.LongLength));
        public virtual bool SendPing(byte[] payload, ushort start, ulong end) => SendPing(PrimitiveBuffer.Slice(payload, start, end));
        protected abstract bool SendPong(byte[] payload);
        public abstract bool SendClose(ushort code, string reason = "");

        protected virtual bool Terminate(ushort code, string reason)
        {
            lock (Sync)
            {
                if (!Opening && !Open && !Closing) return false;
                Base.Terminate();
                FinishClose(code, reason);
                return true;
            }
        }
        public virtual bool Terminate()
        {
            lock (Sync)
            {
                if (!Opening && !Open && !Closing) return false;
                Base.Terminate();
                FinishClose();
                return true;
            }
        }
    }
}
