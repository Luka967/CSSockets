using System;
using CSSockets.Base;
using CSSockets.Streams;

namespace CSSockets.WebSockets
{
    internal enum FrameParserState : byte
    {
        Head1 = 0,
        Head2 = 1,
        ExtendedLen = 2,
        Mask = 3,
        Payload = 4
    }
    class FrameParser : BaseWritable, IQueueableAsyncOutputter<Frame>
    {
        private Queue<Frame> FrameQueue { get; } = new Queue<Frame>();
        public int QueuedCount => throw new System.NotImplementedException();
        public event AsyncCreationHandler<Frame> OnOutput;
        protected void PushIncoming()
        {
            if (OnOutput != null) OnOutput(Incoming);
            else FrameQueue.Enqueue(Incoming);
            Incoming = new Frame();
        }
        public Frame Next()
        {
            ThrowIfEnded();
            if (!FrameQueue.Dequeue(out Frame next))
                // ended
                return null;
            return next;
        }

        private Frame Incoming { get; set; } = new Frame();
        private FrameParserState State { get; set; } = FrameParserState.Head1;
        private long Temp1 { get; set; } = 0;
        private byte[] Temp2 { get; set; } = null;

        protected override void HandleData(byte[] data)
        {
            byte b; long len;
            for (long i = 0; i < data.LongLength;)
            {
                switch (State)
                {
                    case FrameParserState.Head1:
                        b = data[i++];
                        bool fin = Incoming.FIN = (b & 128) == 128;
                        bool rsv1 = Incoming.RSV1 = (b & 64) == 64;
                        bool rsv2 = Incoming.RSV2 = (b & 32) == 32;
                        bool rsv3 = Incoming.RSV3 = (b & 16) == 16;
                        Incoming.Opcode = (byte)(b - (fin ? 128 : 0) - (rsv1 ? 64 : 0) - (rsv2 ? 32 : 0) - (rsv3 ? 16 : 0));
                        State = FrameParserState.Head2;
                        break;
                    case FrameParserState.Head2:
                        b = data[i++];
                        bool masked = (b & 128) == 128;
                        if (masked) Incoming.Mask = new byte[4];
                        long payloadLen = b - (masked ? 128 : 0);
                        if (payloadLen > 125)
                        {
                            Temp2 = new byte[payloadLen == 126 ? 2 : 8];
                            State = FrameParserState.ExtendedLen;
                        }
                        else
                        {
                            Incoming.Payload = new byte[payloadLen];
                            State = Incoming.Masked ? FrameParserState.Mask : FrameParserState.Payload;
                        }
                        break;
                    case FrameParserState.ExtendedLen:
                        len = Math.Min(Temp2.LongLength - Temp1, data.LongLength - i);
                        Frame.ArrayCopy(data, i, Temp2, Temp1, len);
                        Temp1 += len; i += len;
                        if (Temp2.LongLength != Temp1) break;
                        Incoming.Payload = new byte[Temp1 == 8 ? BitConverter.ToUInt64(Temp2, 0) : BitConverter.ToUInt16(Temp2, 0)];
                        Temp1 = 0; Temp2 = null;
                        State = Incoming.Masked ? FrameParserState.Mask : FrameParserState.Payload;
                        break;
                    case FrameParserState.Mask:
                        len = Math.Min(4 - Temp1, data.LongLength - i);
                        Frame.ArrayCopy(data, i, Incoming.Mask, Temp1, len);
                        Temp1 += len; i += len;
                        if (Temp1 != 4) break;
                        Temp1 = 0;
                        State = FrameParserState.Payload;
                        break;
                    case FrameParserState.Payload:
                        len = Math.Min(Incoming.PayloadLength - Temp1, data.LongLength - i);
                        Frame.ArrayCopy(data, i, Incoming.Payload, Temp1, len);
                        Temp1 += len; i += len;
                        if (Temp1 != Incoming.PayloadLength) break;
                        if (Incoming.Masked) Incoming.FlipMask();
                        PushIncoming();
                        State = FrameParserState.Head1;
                        Temp1 = 0;
                        break;
                }
            }
        }

        public override void End()
        {
            base.End();
            FrameQueue.End();
        }
    }
}
