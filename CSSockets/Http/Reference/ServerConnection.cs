using System;
using CSSockets.Tcp;
using CSSockets.Http.Base;

namespace CSSockets.Http.Reference
{
    public delegate void ClientRequestHandler(ClientRequest request, ServerResponse response);
    public sealed class ServerConnection : Connection<RequestHead, ResponseHead>
    {
        static int id = 0;
        int mid = ++id;
        int reqc = 0;
        public ServerConnection(TcpSocket socket, ClientRequestHandler handler) : base(socket)
            => Handler = handler;

        public (ClientRequest req, ServerResponse res)? CurrentMessage { get; private set; } = null;
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
            reqc++;
            hasReqHead = hasResHead = hasReqBody = hasResBody = false;
            HeadParser.OnOutput += OnReqHead;
            HeadParser.Pipe(BodyParser);
            HeadParser.Unpipe();
            BodyParser.Excess.Pipe(HeadParser);
            BodyParser.Excess.Unpipe();
            Base.Pipe(HeadParser);
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

            ClientRequest req = new ClientRequest(head, bodyType.Value, this);
            ServerResponse res = new ServerResponse(head.Version, this);
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
            BodyType? bodyType = BodyType.TryDetectFor(head, true);
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
        private void finishResponse() => FinishResponse(false);
        public override bool FinishResponse() => FinishResponse(true);
        private bool FinishResponse(bool finishBody)
        {
            if (!hasReqHead || !hasResHead) return false;
            OnReqBodyFinish();
            hasResBody = true;

            BodySerializer.Unpipe();
            BodySerializer.OnFinish -= finishResponse;

            CurrentMessage.Value.req.buffer.End();
            CurrentMessage.Value.res.buffer.End();
            WaitHead();
            return true;
        }

        public override bool Detach()
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
            return true;
        }
        public override bool Abandon()
        {
            Detach();
            Base.Unpipe();
            HeadParser.End();
            HeadSerializer.End();
            BodyParser.End();
            BodySerializer.End();
            return true;
        }
    }
}
