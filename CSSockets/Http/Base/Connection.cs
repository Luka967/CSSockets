using System;
using CSSockets.Tcp;
using System.Threading;
using CSSockets.Http.Reference;

namespace CSSockets.Http.Base
{
    public delegate void ControlHandler();
    public delegate void HttpMessageHandler<Incoming, Outgoing>(IncomingMessage<Incoming, Outgoing> request, OutgoingMessage<Incoming, Outgoing> response)
        where Incoming : MessageHead, new() where Outgoing : MessageHead, new();

    abstract public class Connection<Incoming, Outgoing>
        where Incoming : MessageHead, new() where Outgoing : MessageHead, new()
    {
        protected bool Terminating { get; private set; }
        protected bool Upgrading { get; private set; }
        public TcpSocket Base { get; }
        virtual public HttpMessageHandler<Incoming, Outgoing> OnMessage { protected get; set; }
        public event ControlHandler OnEnd;

        protected (IncomingMessage<Incoming, Outgoing>, OutgoingMessage<Incoming, Outgoing>) CurrentMessage { get; set; }

        virtual protected HeadParser<Incoming> HeadParser { get; }
        virtual protected HeadSerializer<Outgoing> HeadSerializer { get; }
        virtual protected BodyParser BodyParser { get; }
        virtual protected BodySerializer BodySerializer { get; }

        public Connection(TcpSocket socket, HeadParser<Incoming> headParser, HeadSerializer<Outgoing> headSerializer)
        {
            Base = socket;
            HeadParser = headParser;
            HeadSerializer = headSerializer;
            BodyParser = new BodyParser();
            BodySerializer = new BodySerializer();
            ThreadPool.QueueUserWorkItem(ProcessorThread);
            Base.OnClose += End;
        }

        abstract protected void ProcessorThread(object _);

        public void WriteHead(Outgoing head)
        {
            if (head.Version == null)
                head.Version = CurrentMessage.Item1.HttpVersion;
            HeadSerializer.Write(head);
            if (!BodySerializer.TrySetFor(head))
                throw new ArgumentException("Could not determine body transfer type from the provided head");
        }

        virtual protected void End()
        {
            HeadParser.Unpipe();
            BodyParser.Unpipe();
            if (!Base.ReadableEnded && !Upgrading)
                Base.Unpipe();
            HeadSerializer.Unpipe();
            BodySerializer.Unpipe();
            HeadParser.End();
            HeadSerializer.End();
            BodyParser.End();
            BodySerializer.End();
            OnEnd?.Invoke();
        }

        virtual public void Terminate()
        {
            if (Terminating) throw new InvalidOperationException("Already terminating");
            Terminating = true;
            Base.Terminate();
        }

        virtual public byte[] SetUpgrading()
        {
            Upgrading = true;
            Base.OnClose -= End;
            byte[] trail = new byte[HeadParser.Buffered + BodyParser.OutgoingBuffered];
            int index = 0;
            if (HeadParser.Buffered > 0)
            {
                byte[] _1 = HeadParser.Read();
                Buffer.BlockCopy(_1, 0, trail, index, _1.Length);
                index += _1.Length;
            }
            if (HeadParser.Buffered > 0)
            {
                byte[] _2 = HeadParser.Read();
                Buffer.BlockCopy(_2, 0, trail, index, _2.Length);
            }
            return trail;
        }
    }
}