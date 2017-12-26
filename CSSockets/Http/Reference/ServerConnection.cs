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

        protected override void ProcessorThread()
        {
            try
            {
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
                        Base.Resume();
                        Base.Pipe(HeadParser);
                    }
                    RequestHead head = HeadParser.Next();
                    if (head == null) break; // disconnecting

                    // repipe
                    if (!BodyParser.TrySetFor(head)) { Terminate(); break; } // badly set body encoding
                    if (BodyParser.TransferEncoding != TransferEncoding.None)
                    {
                        // body exists
                        bool bodyFinished = false;
                        ControlHandler d = () => { Base.Pause(); bodyFinished = true; };
                        BodyParser.OnEnd += d;
                        // HeadParser excess -> BodyParser
                        BodyParser.Write(HeadParser.Read());
                        if (!bodyFinished)
                            // upcoming data -> BodyParser
                            Base.Pipe(BodyParser);
                    }
                    else Base.Pause(); // no body
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

                    // message processed
                    CurrentMessage = (null, null);

                    // check for disconnection
                    if (head.Headers["Connection"] == "close")
                    {
                        End();
                        Base.End();
                        break;
                    }
                    // check if upgrading
                    if (Upgrading)
                    {
                        End();
                        break;
                    }
                }
            }
            catch (ObjectDisposedException) { /* socket got disposed */ }
            if (!HeadParser.Ended) End();
        }
    }
}