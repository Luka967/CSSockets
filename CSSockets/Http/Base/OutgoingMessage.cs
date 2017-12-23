using System;
using System.Threading;
using CSSockets.Streams;

namespace CSSockets.Http.Base
{
    abstract public class OutgoingMessage<Incoming, Outgoing> : IBufferedWritable
        where Incoming : MessageHead, new() where Outgoing : MessageHead, new()
    {
#region BodyBuffer
        internal RawUnifiedDuplex BodyBuffer { get; } = new RawUnifiedDuplex();

        public long WrittenBytes => BodyBuffer.ProcessedBytes;
        public int OutgoingBuffered => BodyBuffer.Buffered;
        public bool Ended => BodyBuffer.Ended;
        public bool Corked => BodyBuffer.Paused;

        public void Unpipe(IReadable from) => BodyBuffer.Unpipe(from);
        public void Cork() => BodyBuffer.Pause();
        public void Uncork() => BodyBuffer.Resume();

        public void Write(byte[] data)
        {
            if (!IsHeadSent) SendHead();
            BodyBuffer.Write(data);
        }
        public void Write(string chunk) => Write(System.Text.Encoding.UTF8.GetBytes(chunk));
        public void Write(byte[] data, int offset, int length) => BodyBuffer.Write(data, offset, length);
        #endregion

        public Outgoing Head { get; set; }
        public bool IsHeadSent { get; internal set; }
        public Connection<Incoming, Outgoing> Connection { get; }
        public event ControlHandler OnEnd;
        internal EventWaitHandle EndWait { get; } = new EventWaitHandle(false, EventResetMode.ManualReset);

        public OutgoingMessage(Connection<Incoming, Outgoing> connection)
        {
            Connection = connection;
            BodyBuffer = new RawUnifiedDuplex();
            Head = new Outgoing();
        }

        protected void ThrowIfHeadSent()
        {
            if (IsHeadSent) throw new InvalidOperationException("Cannot modify head when it's already sent");
        }
        public void SendHead()
        {
            ThrowIfHeadSent();
            Connection.WriteHead(Head);
            IsHeadSent = true;
        }
        public void SetHeader(string name, string value)
        {
            ThrowIfHeadSent();
            Head.Headers[name] = value;
        }

        public string this[string name]
        {
            get => Head.Headers[name];
            set => SetHeader(name, value);
        }

        // ending HttpIncomingMessage ends/sends the response
        public void End()
        {
            // send head if not sent
            if (!IsHeadSent) SendHead();
            // dispose
            BodyBuffer.End();
            OnEnd?.Invoke();
            EndWait.Set();
            EndWait.Dispose();
        }
    }
}
