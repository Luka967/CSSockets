using System.Net;
using CSSockets.Tcp;
using CSSockets.Streams;
using CSSockets.Http.Definition;
using System.Collections.Generic;

namespace CSSockets.Http.Reference
{
    public sealed class ClientConnection : Connection<ResponseHead, RequestHead>
    {
        private OutgoingRequest _Outgoing = null;
        private IncomingResponse _Incoming = null;
        public override OutgoingMessage<ResponseHead, RequestHead> Outgoing => _Outgoing;
        public override IncomingMessage<ResponseHead, RequestHead> Incoming => _Incoming;
        private readonly Queue<OutgoingRequest> unanswered = new Queue<OutgoingRequest>();

        public ClientConnection(EndPoint connectEndPoint, ControlHandler onOpen = null) : this(new Connection())
            => Base.Connect(connectEndPoint);
        public ClientConnection(Connection connection) : base(connection)
        {
            IncomingHead = new ResponseHeadParser();
            IncomingBody = new BodyParser();
            OutgoingHead = new RequestHeadSerializer();
            OutgoingBody = new BodySerializer();
            IncomingHead.OnFail += OnSegmentFail;
            IncomingBody.OnFail += OnSegmentFail;
            OutgoingHead.OnFail += OnSegmentFail;
            OutgoingBody.OnFail += OnSegmentFail;
            IncomingHead.Pipe(excess);
            IncomingBody.Excess.Pipe(excess);
        }

        private readonly MemoryDuplex excess = new MemoryDuplex();
        private bool gotReqHead = false;
        private bool hasReqBody = false;
        private bool pdnResHead = false;
        private bool gotResHead = false;
        private bool gotResBody = false;

        public OutgoingRequest Enqueue(Version version, string method, URL url)
        {
            lock (Sync)
            {
                if (_Outgoing != null) return null;
                gotReqHead = hasReqBody = gotResHead = gotResBody = false;
                OutgoingRequest newPending = new OutgoingRequest(this, version, method, url);
                _Outgoing = newPending;
                unanswered.Enqueue(newPending);
                return newPending;
            }
        }
        public override bool StartOutgoing(RequestHead head)
        {
            lock (Sync)
            {
                if (_Outgoing == null) return false;

                BodyType? type = BodyType.TryDetectFor(head, true);
                if (type == null) return false;
                if (!OutgoingBody.TrySetFor(type.Value)) return false;

                OutgoingHead.Write(head);
                OutgoingHead.Burst(Base);

                if (!OutgoingBody.Finished)
                {
                    hasReqBody = true;
                    OutgoingBody.OnFinish += FinishOutgoingMessage;
                    OutgoingBody.Pipe(Base);
                    _Outgoing.Pipe(OutgoingBody);
                }
                WaitIncoming();
                return gotReqHead = true;
            }
        }
        private void FinishOutgoingMessage()
        {
            if (!FinishOutgoing()) Terminate();
        }
        public override bool FinishOutgoing()
        {
            lock (Sync)
            {
                if (!hasReqBody) return false;
                OutgoingBody.OnFinish -= FinishOutgoingMessage;
                if (!OutgoingBody.Finished) OutgoingBody.Finish();
                _Outgoing.Finish();
                OutgoingBody.Unpipe();
                _Outgoing.Unpipe();
                _Outgoing = null;
                return !(hasReqBody = false);
            }
        }
        private void WaitIncoming()
        {
            lock (Sync)
            {
                if (pdnResHead || unanswered.Count == 0) return;
                pdnResHead = true;
                IncomingHead.OnCollect += PushIncoming;
                excess.Burst(IncomingHead);
                if (!gotResHead) Base.Pipe(IncomingHead);
            }
        }
        private void PushIncoming(ResponseHead head)
        {
            lock (Sync)
            {
                gotResHead = true;
                IncomingHead.OnCollect -= PushIncoming;

                BodyType? type = BodyType.TryDetectFor(head, true);
                if (type == null) { Terminate(); return; }
                if (!IncomingBody.TrySetFor(type.Value)) { Terminate(); return; }

                IncomingResponse incoming = _Incoming = new IncomingResponse(this, head);
                OutgoingRequest outgoing = unanswered.Dequeue();

                outgoing.FireResponse(incoming);
                if (Ended || Frozen) return;

                if (!IncomingBody.Finished)
                {
                    IncomingBody.OnFinish += FinishIncomingMessage;
                    IncomingBody.Pipe(_Incoming);
                    excess.Burst(IncomingBody);
                    if (!gotResBody) Base.Pipe(IncomingBody);
                }
            }
        }
        private void FinishIncomingMessage()
        {
            lock (Sync)
            {
                gotResBody = true;
                IncomingBody.OnFinish -= FinishIncomingMessage;
                _Incoming.Finish();
                _Incoming = null;
                Base.Unpipe();
                pdnResHead = false;
                WaitIncoming();
            }
        }
        private void OnSegmentFail() => Terminate();
        public override byte[] Freeze()
        {
            lock (Sync)
            {
                _Incoming = null; _Outgoing = null; Frozen = true; unanswered.Clear();
                gotReqHead = hasReqBody = pdnResHead = gotResHead = gotResBody = false;
                Base.Unpipe();
                IncomingHead.Unpipe();
                IncomingHead.OnFail -= OnSegmentFail;
                if (pdnResHead) IncomingHead.OnCollect -= PushIncoming;
                IncomingBody.Unpipe();
                IncomingBody.Excess.Unpipe();
                IncomingBody.OnFail -= OnSegmentFail;
                if (!hasReqBody) IncomingBody.OnFinish -= FinishIncomingMessage;
                OutgoingHead.Unpipe();
                OutgoingHead.OnFail -= OnSegmentFail;
                OutgoingBody.Unpipe();
                OutgoingBody.OnFail -= OnSegmentFail;
                if (!gotResBody) OutgoingBody.OnFinish -= FinishOutgoingMessage;
                return excess.Read();
            }
        }
        public override bool End()
        {
            lock (Sync)
            {
                if (!Ended) return false;
                if (!Frozen) Freeze();
                return Ended = true;
            }
        }
    }
}
