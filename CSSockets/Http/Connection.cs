using System;
using CSSockets.Tcp;
using System.Threading;

namespace CSSockets.Http
{
    public delegate void ControlHandler();
    public delegate void HttpMessageHandler<Incoming, Outgoing>(HttpIncomingMessage<Incoming, Outgoing> request, HttpOutgoingMessage<Incoming, Outgoing> response)
        where Incoming : HttpHead, new() where Outgoing : HttpHead, new();

    abstract public class HttpConnection<Incoming, Outgoing>
        where Incoming : HttpHead, new() where Outgoing : HttpHead, new()
    {
        protected bool Terminating { get; private set; }
        public TcpSocket Base { get; }
        virtual public HttpMessageHandler<Incoming, Outgoing> OnMessage { protected get; set; }

        protected (HttpIncomingMessage<Incoming, Outgoing>, HttpOutgoingMessage<Incoming, Outgoing>) CurrentMessage { get; set; }

        virtual protected HeadParser<Incoming> HeadParser { get; }
        virtual protected HeadSerializer<Outgoing> HeadSerializer { get; }
        virtual protected BodyParser BodyParser { get; }
        virtual protected BodySerializer BodySerializer { get; }

        public HttpConnection(TcpSocket socket, HeadParser<Incoming> headParser, HeadSerializer<Outgoing> headSerializer)
        {
            Base = socket;
            HeadParser = headParser;
            HeadSerializer = headSerializer;
            BodyParser = new BodyParser();
            BodySerializer = new BodySerializer();
            ThreadPool.QueueUserWorkItem(ProcessorThread);
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

        virtual public void Terminate()
        {
            Terminating = true;
            Base.Terminate();
            HeadParser.End();
            HeadSerializer.End();
            BodyParser.End();
            BodySerializer.End();
        }
    }

    sealed public class HttpServerConnection : HttpConnection<HttpRequestHead, HttpResponseHead>
    {
        public HttpServerConnection(TcpSocket socket) : base(socket, new RequestHeadParser(), new ResponseHeadSerializer())
        { }

        protected override void ProcessorThread(object _)
        {
            while (!Terminating)
            {
                Base.Uncork();
                Base.Pipe(HeadParser);
                HttpRequestHead head = HeadParser.Next();
                if (!BodyParser.TrySetFor(head))
                {
                    // bad head
                    Terminate();
                    break;
                }

                BodyParser.OnEnd += () =>
                {
                    // body is done
                    Base.Unpipe();
                    Base.Cork();
                };
                // head is done, write excess data and repipe Base
                Base.Cork();
                if (HeadParser.Buffered > 0)
                    // excess in HeadParser
                    BodyParser.Write(HeadParser.Read());
                Base.Pipe(BodyParser);
                Base.Uncork();

                HeadSerializer.Pipe(Base);
                BodySerializer.Pipe(Base);

                // get new message, pipe it and create handlers
                HttpClientRequest req = new HttpClientRequest(this, head); 
                HttpServerResponse res = new HttpServerResponse(this);
                CurrentMessage = (req, res);

                BodyParser.Pipe(req.BodyBuffer);
                res.BodyBuffer.Pipe(BodySerializer);
                req.OnEnd += Terminate;

                OnMessage?.Invoke(req, res);
                if (req.Cancelled) break; // terminated
                if (!res.Ended) res.EndWait.WaitOne();
                if (!res.IsHeadSent) res.SendHead();

                // unpipe serializers
                HeadSerializer.Unpipe();
                BodySerializer.Unpipe();
            }
        }
    }
}