using System;
using CSSockets.Tcp;
using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class ServerConnection : Connection<RequestHead, ResponseHead>
    {
        public ServerConnection(TcpSocket socket) : base(socket, new RequestHeadParser(), new ResponseHeadSerializer())
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
                    RequestHead head = HeadParser.Next();
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
                    ClientRequest req = new ClientRequest(this, head);
                    ServerResponse res = new ServerResponse(this);
                    CurrentMessage = (req, res);
                    // pipe body buffers
                    BodyParser.Pipe(req.BodyBuffer);
                    res.BodyBuffer.Pipe(BodySerializer);
                    req.OnEnd += Terminate;

                    OnMessage?.Invoke(req, res);
                    if (req.Cancelled) break; // terminated
                    if (!res.Ended) res.EndWait.WaitOne(); // wait for end

                    // end body buffers
                    if (!req.Ended) req.BodyBuffer.End();
                    if (!res.Ended) res.BodyBuffer.End();

                    // finish body
                    BodySerializer.Finish();

                    // unpipe serializers
                    HeadSerializer.Unpipe();
                    BodySerializer.Unpipe();
                }
            }
            catch (ObjectDisposedException) { /* socket got disposed */ }
            if (!HeadParser.Ended) End();
        }
    }
}