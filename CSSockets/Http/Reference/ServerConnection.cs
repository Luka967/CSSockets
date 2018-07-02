using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Definition;

namespace CSSockets.Http.Reference
{
    public delegate void IncomingRequestHandler(IncomingRequest request, OutgoingResponse response);
    public sealed class ServerConnection : Connection<RequestHead, ResponseHead>
    {
        private IncomingRequest _Incoming = null;
        private OutgoingResponse _Outgoing = null;
        public override IncomingMessage<RequestHead, ResponseHead> Incoming => _Incoming;
        public override OutgoingMessage<RequestHead, ResponseHead> Outgoing => _Outgoing;

        public IncomingRequestHandler RequestHandler { get; set; }

        public ServerConnection(Connection connection, IncomingRequestHandler requestHandler) : base(connection)
        {
            IncomingHead = new RequestHeadParser();
            OutgoingHead = new ResponseHeadSerializer();
            IncomingBody = new BodyParser();
            OutgoingBody = new BodySerializer();
            IncomingHead.OnFail += OnSegmentFail;
            IncomingBody.OnFail += OnSegmentFail;
            OutgoingHead.OnFail += OnSegmentFail;
            OutgoingBody.OnFail += OnSegmentFail;
            IncomingHead.Pipe(excess);
            IncomingBody.Excess.Pipe(excess);
            RequestHandler = requestHandler;
            WaitIncoming();
        }

        private readonly MemoryDuplex excess = new MemoryDuplex();
        private bool gotReqHead = false;
        private bool gotReqBody = false;
        private bool gotResHead = false;
        private bool hasResBody = false;

        private void WaitIncoming()
        {
            lock (Sync)
            {
                _Incoming = null; _Outgoing = null;
                gotReqHead = gotReqBody = gotResHead = false;
                IncomingHead.OnCollect += PushIncoming;
                excess.Burst(IncomingHead);
                if (!gotReqHead) Base.Pipe(IncomingHead);
            }
        }
        private void PushIncoming(RequestHead head)
        {
            lock (Sync)
            {
                gotReqHead = true;

                BodyType? type = BodyType.TryDetectFor(head, true);
                if (type == null) { Terminate(); return; }
                if (!IncomingBody.TrySetFor(type.Value)) { Terminate(); return; }

                IncomingRequest incoming = _Incoming = new IncomingRequest(this, head);
                OutgoingResponse outgoing = _Outgoing = new OutgoingResponse(this, head.Version);

                RequestHandler(incoming, outgoing);
                if (Ended || Frozen) return;

                if (!IncomingBody.Finished)
                {
                    IncomingBody.Pipe(_Incoming);
                    IncomingBody.OnFinish += FinishIncomingMessage;
                    excess.Burst(IncomingBody);
                    if (!gotReqBody) Base.Pipe(IncomingBody);
                }
            }
        }
        private void FinishIncomingMessage()
        {
            lock (Sync)
            {
                gotReqBody = true;
                IncomingBody.OnFinish -= FinishIncomingMessage;
                IncomingBody.Unpipe();
                _Incoming.Finish();
                Base.Unpipe();
            }
        }
        public override bool StartOutgoing(ResponseHead head)
        {
            lock (Sync)
            {
                if (!gotReqHead) return false;

                BodyType? type = BodyType.TryDetectFor(head, true);
                if (type == null) return false;
                if (!OutgoingBody.TrySetFor(type.Value)) return false;

                OutgoingHead.Write(head);
                OutgoingHead.Burst(Base);

                if (!OutgoingBody.Finished)
                {
                    hasResBody = true;
                    OutgoingBody.OnFinish += FinishOutgoingMessage;
                    OutgoingBody.Pipe(Base);
                    _Outgoing.Pipe(OutgoingBody);
                }
                return gotResHead = true;
            }
        }
        public override bool FinishOutgoing()
        {
            lock (Sync)
            {
                if (!gotResHead) return false;
                OutgoingBody.OnFinish -= FinishOutgoingMessage;
                if (!OutgoingBody.Finished) OutgoingBody.Finish();
                OutgoingBody.Unpipe();
                _Outgoing.Finish();
                _Incoming = null;
                _Outgoing = null;
                WaitIncoming();
                return !(hasResBody = false);
            }
        }
        private void FinishOutgoingMessage()
        {
            if (!FinishOutgoing()) Terminate();
        }
        private void OnSegmentFail() => Terminate();
        public override byte[] Freeze()
        {
            lock (Sync)
            {
                _Incoming = null; _Outgoing = null; Frozen = true;
                gotReqHead = gotReqBody = gotResHead = hasResBody = false;
                Base.Unpipe();
                IncomingHead.Unpipe();
                IncomingHead.OnFail -= OnSegmentFail;
                if (!gotReqHead) IncomingHead.OnCollect -= PushIncoming;
                IncomingBody.Unpipe();
                IncomingBody.Excess.Unpipe();
                IncomingBody.OnFail -= OnSegmentFail;
                if (!gotReqBody) IncomingBody.OnFinish -= FinishIncomingMessage;
                OutgoingHead.Unpipe();
                OutgoingHead.OnFail -= OnSegmentFail;
                OutgoingBody.Unpipe();
                OutgoingBody.OnFail -= OnSegmentFail;
                if (!hasResBody) OutgoingBody.OnFinish -= FinishOutgoingMessage;
                return excess.Read();
            }
        }
    }
}
