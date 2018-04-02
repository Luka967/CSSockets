using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Base;

namespace CSSockets.Http.Reference
{
    public delegate void ClientRequestHandler(IncomingRequest request, OutgoingResponse response);
    public sealed class ServerConnection : Connection<RequestHead, ResponseHead>
    {
        public ServerConnection(Connection connection, ClientRequestHandler handler) : base(connection) => Handler = handler;

        public (IncomingRequest req, OutgoingResponse res)? CurrentMessage { get; private set; } = null;
        public ClientRequestHandler Handler { get; set; }
        internal bool upgrading = false;
        private bool hasReqHead = false;
        private bool hasResHead = false;
        private bool hasReqBody = false;
        private bool hasResBody = false;

        protected override bool Initialize()
        {
            HeadParser = new RequestHeadParser();
            HeadSerializer = new ResponseHeadSerializer();
            BodyParser = new BodyParser();
            BodySerializer = new BodySerializer();
            HeadParser.OnFail += _terminate;
            BodyParser.OnFail += _terminate;
            WaitHead();
            return true;
        }
        private void _terminate() => Terminate();

        private void WaitHead()
        {
            hasReqHead = hasResHead = hasReqBody = hasResBody = false;
            HeadParser.OnOutput += OnReqHead;
            HeadParser.Pipe(HeadParser);
            HeadParser.Unpipe();
            BodyParser.Excess.Pipe(!hasReqHead ? HeadParser as IWritable : BodyParser as IWritable);
            BodyParser.Excess.Unpipe();
            Base.Pipe(!hasReqHead ? HeadParser as IWritable : BodyParser as IWritable);
        }
        private void OnReqHead(RequestHead head)
        {
            if (hasReqHead) return;
            hasReqHead = true;
            Base.Unpipe();
            HeadParser.OnOutput -= OnReqHead;

            BodyType? bodyType = BodyType.TryDetectFor(head, true);
            if (bodyType == null) { Terminate(); return; }
            if (!BodyParser.TrySetFor(bodyType.Value)) { Terminate(); return; }

            IncomingRequest req = new IncomingRequest(head, bodyType.Value, this);
            OutgoingResponse res = new OutgoingResponse(head.Version, this);
            CurrentMessage = (req, res);

            BodyParser.OnFinish += OnReqBodyFinish;
            BodyParser.Pipe(req.buffer);
            HeadParser.Pipe(BodyParser);
            HeadParser.Unpipe();
            if (!hasReqBody) Base.Pipe(BodyParser);

            HeadSerializer.Pipe(Base);
            Handler(req, res);
        }
        private void OnReqBodyFinish()
        {
            if (!hasReqHead) return;
            hasReqBody = true;
            BodyParser.OnFinish -= OnReqBodyFinish;
            BodyParser.Unpipe();
        }
        public override bool SendHead(ResponseHead head)
        {
            if (!hasReqHead) return false;
            BodyType? bodyType = BodyType.TryDetectFor(head, false);
            if (bodyType == null) return !Terminate();
            if (!BodySerializer.TrySetFor(bodyType.Value)) return !Terminate();
            hasResHead = true;

            HeadSerializer.Write(head);
            HeadSerializer.Unpipe();

            BodySerializer.OnFinish += finishResponse;

            BodySerializer.Pipe(Base);
            CurrentMessage.Value.res.buffer.Pipe(BodySerializer);
            return true;
        }
        public bool SendContinue()
        {
            if (!hasReqHead) return false;
            ResponseHeadSerializer serializer = new ResponseHeadSerializer();
            serializer.Pipe(Base);
            serializer.Write(new ResponseHead()
            {
                StatusCode = 100,
                StatusDescription = "Continue",
                Version = CurrentMessage.Value.req.Version
            });
            serializer.End();
            return true;
        }
        private void finishResponse() => FinishResponse(false);
        public override bool FinishResponse() => FinishResponse(true);
        private bool FinishResponse(bool finishBody)
        {
            if (!hasReqHead || !hasResHead) return finishBody;
            OnReqBodyFinish();
            hasResBody = true;

            BodySerializer.OnFinish -= finishResponse;
            CurrentMessage.Value.req.buffer.End();
            CurrentMessage.Value.res.buffer.End();
            CurrentMessage = null;
            if (finishBody)
            {
                if (BodySerializer.ContentLength == null && BodySerializer.TransferEncoding == TransferEncoding.Raw)
                    // unknown body size - close the connection to signal end of body
                    return BodySerializer.Finish() && Freeze() && Abandon() && Base.End();
                BodySerializer.Finish();
            }
            BodySerializer.Unpipe();
            WaitHead();
            return true;
        }

        public override bool Freeze()
        {
            HeadParser.OnFail -= _terminate;
            BodyParser.OnFail -= _terminate;
            if (!hasReqHead) HeadParser.OnOutput -= OnReqHead;
            if (hasReqHead && !hasReqBody) BodyParser.OnFinish -= OnReqBodyFinish;
            if (hasResHead && !hasResBody) BodySerializer.OnFinish -= finishResponse;
            HeadParser.Unpipe();
            HeadSerializer.Unpipe();
            BodyParser.Unpipe();
            BodySerializer.Unpipe();
            hasReqHead = hasReqBody = hasResHead = hasResBody = false;
            return true;
        }
        public override bool Abandon()
        {
            Freeze();
            CurrentMessage?.req.buffer.End();
            CurrentMessage?.res.buffer.End();
            CurrentMessage = null;
            Base.Unpipe();
            HeadParser.End();
            HeadSerializer.End();
            BodyParser.End();
            BodySerializer.End();
            return true;
        }
    }
}
