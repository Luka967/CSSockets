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
        public event ControlHandler OnEnd;

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
    }

    sealed public class HttpServerConnection : HttpConnection<HttpRequestHead, HttpResponseHead>
    {
        public HttpServerConnection(TcpSocket socket) : base(socket, new RequestHeadParser(), new ResponseHeadSerializer())
        { }

        protected override void ProcessorThread(object _)
        {
            try
            {
                // self-removing OnEnd for BodyParser
                ControlHandler d = null;
                d = () => { Base.Cork(); BodyParser.OnEnd -= d; };
                while (!Terminating)
                {
                    // HeadParser excess -> HeadParser
                    if (HeadParser.Buffered > 0)
                        HeadParser.Write(HeadParser.Read());
                    if (HeadParser.QueuedCount == 0)
                    {
                        // BodyParser excess -> HeadParser
                        if (BodyParser.OutgoingBuffered > 0)
                            HeadParser.Write(BodyParser.ReadExcess());
                    }
                    if (HeadParser.QueuedCount == 0)
                    {
                        // upcoming data -> HeadParser
                        Base.Uncork();
                        Base.Pipe(HeadParser);
                    }
                    HttpRequestHead head = HeadParser.Next();
                    if (head == null) break; // disconnecting

                    // repipe
                    if (!BodyParser.TrySetFor(head)) { Terminate(); break; } // badly set body encoding
                    if (BodyParser.TransferEncoding != TransferEncoding.None)
                    {
                        // body exists
                        BodyParser.OnEnd += d;
                        if (HeadParser.Buffered > 0)
                            // HeadParser excess -> BodyParser
                            BodyParser.Write(HeadParser.Read());
                        if (BodyParser.TransferEncoding != TransferEncoding.None)
                            // upcoming data -> BodyParser
                            Base.Pipe(BodyParser);
                    }
                    else Base.Cork(); // no body
                    HeadSerializer.Pipe(Base);
                    BodySerializer.Pipe(Base);

                    // create request
                    HttpClientRequest req = new HttpClientRequest(this, head);
                    HttpServerResponse res = new HttpServerResponse(this);
                    CurrentMessage = (req, res);
                    // pipe body buffers
                    BodyParser.Pipe(req.BodyBuffer);
                    res.BodyBuffer.Pipe(BodySerializer);
                    req.OnEnd += Terminate;

                    OnMessage?.Invoke(req, res);
                    if (req.Cancelled) break; // terminated
                    if (!res.Ended) res.EndWait.WaitOne(); // wait for end

                    // unpipe serializers
                    HeadSerializer.Unpipe();
                    BodySerializer.Unpipe();
                }
            }
            // socket got disposed
            catch (ObjectDisposedException) { }
            finally
            {
                if (!HeadParser.Ended) End();
            }
        }
    }
}