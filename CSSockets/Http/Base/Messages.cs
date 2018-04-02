using System;
using CSSockets.Streams;
using System.IO.Compression;
using CSSockets.Http.Reference;
using CSSockets.Http.Structures;

namespace CSSockets.Http.Base
{
    public abstract class IncomingMessage<TParse, TSerialize> : IBufferedReadable
        where TParse : Head, new()
        where TSerialize : Head, new()
    {
        public TParse Head { get; }
        public Structures.Version Version => Head.Version;
        public HeaderCollection Headers => Head.Headers;
        public string this[string key]
        {
            get => Head.Headers[key];
            set => Head.Headers[key] = value;
        }
        public Header this[int index]
        {
            get => Head.Headers[index];
            set => Head.Headers[index] = value;
        }

        protected IncomingMessage(TParse head, BodyType bodyType, Connection<TParse, TSerialize> connection)
        {
            Head = head;
            this.bodyType = bodyType;
            Connection = connection;
        }

        public Connection<TParse, TSerialize> Connection { get; }
        internal readonly MemoryDuplex buffer = new MemoryDuplex();
        protected readonly BodyType bodyType;
        public TransferEncoding TransferEncoding => bodyType.TransferEncoding;
        public CompressionType CompressionType => bodyType.CompressionType;
        public ulong? ContentLength => Connection.BodyParser.ContentLength;

        public event DataHandler OnData { add => buffer.OnData += value; remove => buffer.OnData -= value; }
        public event ControlHandler OnFail { add => buffer.OnFail += value; remove => buffer.OnFail -= value; }
        public event ControlHandler OnEnd { add => buffer.OnEnd += value; remove => buffer.OnEnd -= value; }
        public bool Ended => buffer.Ended;
        public bool IsPaused => buffer.IsPaused;
        public IWritable PipedTo => buffer.PipedTo;
        public ulong ReadCount => buffer.WriteCount;
        public ulong BufferedReadable => buffer.Buffered;
        public byte[] Read() => buffer.Read();
        public byte[] Read(ulong length) => buffer.Read(length);
        public ulong Read(byte[] destination) => buffer.Read(destination);
        public bool Pipe(IWritable to) => buffer.Pipe(to);
        public bool Unpipe() => buffer.Unpipe();
        public bool Pause() => buffer.Pause();
        public bool Resume() => buffer.Resume();
        public virtual bool End() => Connection.Terminate();
    }
    public abstract class OutgoingMessage<TParse, TSerialize> : IBufferedWritable
        where TParse : Head, new()
        where TSerialize : Head, new()
    {
        protected OutgoingMessage(Structures.Version version, Connection<TParse, TSerialize> connection)
        {
            Version = version;
            Connection = connection;
        }

        protected TSerialize head = new TSerialize();
        public TSerialize Head { get => head; set => head = value; }
        public Structures.Version Version { get => head.Version; set => head.Version = value; }
        public HeaderCollection Headers => head.Headers;
        public string this[string key]
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.Headers[key];
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.Headers[key] = value; }
        }
        public Header this[int index]
        {
            get => IsHeadSent ? throw new InvalidOperationException("Head already sent") : head.Headers[index];
            set { if (IsHeadSent) throw new InvalidOperationException("Head already sent"); head.Headers[index] = value; }
        }

        public Connection<TParse, TSerialize> Connection { get; }
        internal readonly MemoryDuplex buffer = new MemoryDuplex();
        public bool IsHeadSent { get; protected set; } = false;
        public bool IsContinueSent { get; protected set; } = false;
        public bool SendHead()
        {
            if (!Connection.SendHead(head)) return false;
            return IsHeadSent = true;
        }
        public bool SetCompression(CompressionType compression, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            if (!IsHeadSent && !SendHead()) return false;
            return Connection.BodySerializer.SetCompression(compression, compressionLevel);
        }

        public bool Ended => buffer.Ended;
        public bool IsCorked => buffer.IsPaused;
        public ulong WriteCount => buffer.WriteCount;
        public ulong BufferedWritable => buffer.Buffered;
        public event ControlHandler OnEnd { add => buffer.OnEnd += value; remove => buffer.OnEnd -= value; }
        public event ControlHandler OnDrain { add => buffer.OnDrain += value; remove => buffer.OnDrain -= value; }
        public bool Write(byte[] source)
        {
            if (!IsHeadSent && !SendHead()) return false;
            return buffer.Write(source);
        }
        public bool Write(byte[] source, ulong start, ulong end)
        {
            if (!IsHeadSent && !SendHead()) return false;
            return buffer.Write(source, start, end);
        }
        public bool Write(string source, System.Text.Encoding encoding) => Write(encoding.GetBytes(source));
        public bool Write(string source) => Write(System.Text.Encoding.UTF8.GetBytes(source));
        public bool Unpipe(IReadable from) => buffer.Unpipe(from);
        public bool Cork() => buffer.Pause();
        public bool Uncork() => buffer.Resume();

        public virtual bool End() => !IsHeadSent ? !SendHead() && Connection.FinishResponse() : Connection.FinishResponse();
    }
}
