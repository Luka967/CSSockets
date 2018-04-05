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
    public abstract partial class WebSocket : IPausable, ICorkable
    {
        public Connection Base { get; set; }
        public RequestHead RequestHead { get; }
        public TcpSocketState State => Base.State;
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
        protected bool IsStreaming { get; private set; } = false;
        protected bool StreamerSentFirst { get; private set; } = false;
        protected object OpsLock { get; } = new object();
        protected FrameParser FrameParser { get; } = new FrameParser();
        protected FrameMerger FrameMerger { get; } = new FrameMerger();
        protected abstract FrameBehavior Behavior { get; }

        protected WebSocket(Connection socket, RequestHead head)
        {
            Base = socket;
            Base.OnClose += OnSurpriseEnd;
            Base.OnClose += OnSocketEnd;
            RequestHead = head;
            FrameParser.OnOutput += OnIncomingFrame;
            FrameMerger.OnOutput += OnIncomingMessage;
        }
        internal void WriteTrail(byte[] trail)
        {
            FrameParser.Write(trail);
            Base.Pipe(FrameParser);
        }

        private void OnIncomingFrame(Frame frame)
        {
            if (!Behavior.Test(frame))
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
                case FrameMergeResponse.Success: break;
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
                        ushort code = (ushort)(message.Data.Length == 0 ? 0 : message.Data[0] * 256 + message.Data[1]);
                        string reason = message.Data.Length >= 2 ? Encoding.UTF8.GetString(message.Data, 2, message.Data.Length - 2) : null;
                        byte[] payload = new byte[2 + reason.Length];
                        payload[0] = message.Data[0];
                        payload[1] = message.Data[1];
                        Send(Behavior.Get(true, 8, payload));
                        InitiateClose(code, reason);
                        break;
                    case 9:
                        OnPing?.Invoke(message.Data);
                        Send(Behavior.Get(true, 10, message.Data));
                        break;
                    case 10: OnPong?.Invoke(message.Data); break;
                    default: ForciblyClose(); break;
                }
            }
        }

        public virtual bool Cork() => Base.Pause();
        public virtual bool Uncork() => Base.Resume();
        public virtual bool Pause() => Base.Pause();
        public virtual bool Resume() => Base.Resume();

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
            if (IsClosing) return;
            frame.Serialize(Base);
        }

        public Streamer Stream()
        {
            lock (OpsLock)
            {
                if (IsStreaming) throw new InvalidOperationException("Already streaming");
                IsStreaming = true;
                return new Streamer(this);
            }
        }
        public Streamer<T> Stream<T>() where T : UnifiedDuplex, new()
        {
            lock (OpsLock)
            {
                if (IsStreaming) throw new InvalidOperationException("Already streaming");
                IsStreaming = true;
                return new Streamer<T>(this);
            }
        }

        public virtual void Send(byte[] data)
        {
            lock (OpsLock)
            {
                if (StreamerSentFirst) throw new InvalidOperationException("Cannot send a message while streaming");
                Send(Behavior.Get(true, 2, data));
            }
        }
        public virtual void Send(string data)
        {
            lock (OpsLock)
            {
                if (StreamerSentFirst) throw new InvalidOperationException("Cannot send a message while streaming");
                Send(Behavior.Get(true, 1, Encoding.UTF8.GetBytes(data)));
            }
        }
        public virtual void Ping(byte[] data = null)
        {
            lock (OpsLock)
            {
                if (StreamerSentFirst) throw new InvalidOperationException("Cannot ping while streaming");
                Send(Behavior.Get(true, 9, data));
            }
        }
        public virtual void Close(ushort code, string reason = null)
        {
            reason = reason ?? "";
            lock (OpsLock)
            {
                if (StreamerSentFirst) throw new InvalidOperationException("Cannot close while streaming");
                byte[] reasonBuf = Encoding.UTF8.GetBytes(reason);
                byte[] payload = new byte[2 + reason.Length];
                payload[0] = (byte)(code / 256);
                payload[1] = (byte)(code & 256);
                PrimitiveBuffer.Copy(reasonBuf, 0, payload, 2, reasonBuf.Length);
                Send(Behavior.Get(true, 8, payload));
                InitiateClose(code, reason);
            }
        }
    }

    public class ServerWebSocket : WebSocket
    {
        private static FrameBehavior behavior = new FrameBehavior(false, false, false, true, false, false, false, false);
        protected override FrameBehavior Behavior => behavior;

        public ServerWebSocket(Connection socket, RequestHead head) : base(socket, head) { }
    }

    public class ClientWebSocket : WebSocket
    {
        private static FrameBehavior behavior = new FrameBehavior(false, false, false, false, false, false, false, true);
        protected override FrameBehavior Behavior => behavior;

        public ClientWebSocket(Connection socket, RequestHead head) : base(socket, head) { }
    }
}
