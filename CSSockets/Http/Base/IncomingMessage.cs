using System;
using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Base
{
    abstract public class IncomingMessage<Incoming, Outgoing> : IBufferedReadable
        where Incoming : MessageHead, new() where Outgoing : MessageHead, new()
    {
#region BodyBuffer
        internal RawUnifiedDuplex BodyBuffer { get; } = new RawUnifiedDuplex();

        public long ReadBytes => BodyBuffer.ProcessedBytes;
        public int IncomingBuffered => BodyBuffer.Buffered;
        public IWritable PipedTo => BodyBuffer.PipedTo;
        public bool Ended => BodyBuffer.Ended;
        public bool Paused => BodyBuffer.Paused;

        public event DataHandler OnData
        {
            add => BodyBuffer.OnData += value;
            remove => BodyBuffer.OnData -= value;
        }

        public void Pause() => BodyBuffer.Pause();
        public void Resume() => BodyBuffer.Resume();

        public void Pipe(IWritable to) => BodyBuffer.Pipe(to);
        public void Unpipe() => BodyBuffer.Unpipe();

        public byte[] Read() => BodyBuffer.Read();
        public byte[] Read(int length) => BodyBuffer.Read(length);
        #endregion

        public Incoming Head { get; set; }
        public bool Cancelled { get; internal set; }
        public bool HasBody { get; }
        public Connection<Incoming, Outgoing> Connection { get; }
        public event ControlHandler OnEnd;

        public IncomingMessage(Connection<Incoming, Outgoing> connection, Incoming head, bool hasBody)
        {
            Connection = connection;
            BodyBuffer = new RawUnifiedDuplex();
            Head = head;
            HasBody = hasBody;
        }

        // head accessors
        public HttpVersion HttpVersion => Head.Version;
        public string this[string name] => Head.Headers[name];

        virtual public void SetTimeout(TimeSpan span, TcpSocketControlHandler callback)
        {
            Connection.Base.CanTimeout = true;
            Connection.Base.OnTimeout += () =>
            {
                callback();
                Connection.Base.End();
            };
        }

        // ending HttpIncomingMessage terminates the connection
        public void End()
        {
            BodyBuffer.End();
            OnEnd?.Invoke();
            Cancelled = true;
        }
    }
}
