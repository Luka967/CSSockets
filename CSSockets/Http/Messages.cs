using System;
using System.Threading;
using CSSockets.Streams;
using System.Collections.Generic;

namespace CSSockets.Http
{
    abstract public class HttpIncomingMessage<Incoming, Outgoing> : IBufferedReadable
        where Incoming : HttpHead, new() where Outgoing : HttpHead, new()
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

        protected Incoming Head { get; set; }
        public bool Cancelled { get; internal set; }
        public HttpConnection<Incoming, Outgoing> Connection { get; }
        public event ControlHandler OnEnd;

        public HttpIncomingMessage(HttpConnection<Incoming, Outgoing> connection, Incoming head)
        {
            Connection = connection;
            BodyBuffer = new RawUnifiedDuplex();
            Head = head;
        }

        // head accessors
        public Version HttpVersion => Head.Version;
        public string this[string name] => Head.Headers[name];

        // ending HttpIncomingMessage terminates the connection
        public void End()
        {
            BodyBuffer.End();
            OnEnd?.Invoke();
            Cancelled = true;
        }
    }

    public class HttpClientRequest : HttpIncomingMessage<HttpRequestHead, HttpResponseHead>
    {
        public HttpClientRequest(HttpConnection<HttpRequestHead, HttpResponseHead> connection, HttpRequestHead head) : base(connection, head) { }

        // head accesors
        public Query Query => Head.Query;
        public string Method => Head.Method;
    }

    abstract public class HttpOutgoingMessage<Incoming, Outgoing> : IBufferedWritable
        where Incoming : HttpHead, new() where Outgoing : HttpHead, new()
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

        public void Write(byte[] data) => BodyBuffer.Write(data);
        public void Write(byte[] data, int offset, int length) => BodyBuffer.Write(data, offset, length);

#endregion

        protected Outgoing Head { get; set; }
        public bool IsHeadSent { get; internal set; }
        public HttpConnection<Incoming, Outgoing> Connection { get; }
        public event ControlHandler OnEnd;
        internal EventWaitHandle EndWait { get; } = new EventWaitHandle(false, EventResetMode.ManualReset);

        public HttpOutgoingMessage(HttpConnection<Incoming, Outgoing> connection)
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

    public class HttpServerResponse : HttpOutgoingMessage<HttpRequestHead, HttpResponseHead>
    {
        public HttpServerResponse(HttpConnection<HttpRequestHead, HttpResponseHead> connection) : base(connection) { }

        // head accesors
        public ushort StatusCode
        {
            get => Head.StatusCode;
            set { ThrowIfHeadSent(); Head.StatusCode = value; }
        }
        public string StatusDescription
        {
            get => Head.StatusDescription;
            set { ThrowIfHeadSent(); Head.StatusDescription = value; }
        }

        public void SetHead(ushort statusCode, string statusDescription, IEnumerable<Header> headers = null)
        {
            ThrowIfHeadSent();
            Head.StatusCode = statusCode;
            Head.StatusDescription = statusDescription;
            if (headers != null) foreach (Header h in headers)
                    Head.Headers[h.Name] = h.Value;
        }
    }
}
