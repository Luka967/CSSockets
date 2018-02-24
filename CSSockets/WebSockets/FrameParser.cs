﻿using System;
using CSSockets.Streams;
using CSSockets.Http.Base;

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
    public class FrameParser : BaseWritable, IQueueingOutputter<Frame>
    {
        private Queue<Frame> FrameQueue { get; } = new Queue<Frame>();
        public int Queued => FrameQueue.Count;
        public event OutputterHandler<Frame> OnOutput;
        protected void PushIncoming()
        {
            if (OnOutput != null) OnOutput(Incoming);
            else FrameQueue.Enqueue(Incoming);
            Incoming = new Frame();
        }
        public Frame Next()
        {
            if (Ended) return null;
            if (!FrameQueue.Dequeue(out Frame next))
                // ended
                return null;
            return next;
        }

        private Frame Incoming { get; set; } = new Frame();
        private FrameParserState State { get; set; } = FrameParserState.Head1;
        private ulong Temp1 { get; set; } = 0;
        private byte[] Temp2 { get; set; } = null;

        protected override void HandleData(byte[] data)
        {
            byte b; ulong len, dataLen = (ulong)data.LongLength;
            for (ulong i = 0; i < dataLen;)
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
                        byte payloadLen = (byte)(b - (masked ? 128 : 0));
                        if (payloadLen >= 126)
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
                        ulong tempLen = (ulong)Temp2.LongLength;
                        len = Math.Min(tempLen - Temp1, dataLen - i);
                        PrimitiveBuffer.Copy(data, i, Temp2, Temp1, len);
                        Temp1 += len; i += len;
                        if (tempLen != Temp1) break;
                        len = 0;
                        if (Temp2.LongLength == 2)
                            len = Temp2[0] * 256u + Temp2[1];
                        else len = Temp2[0] * 72057594037927940u + Temp2[1] * 281474976710656u + Temp2[2] * 1099511627776u
                                + Temp2[3] * 4294967296u + Temp2[4] * 16777216u + Temp2[5] * 65536u + Temp2[6] * 256u + Temp2[7];
                        Incoming.Payload = new byte[len];
                        Temp1 = 0; Temp2 = null;
                        State = Incoming.Masked ? FrameParserState.Mask : FrameParserState.Payload;
                        break;
                    case FrameParserState.Mask:
                        len = Math.Min(4 - Temp1, dataLen - i);
                        PrimitiveBuffer.Copy(data, i, Incoming.Mask, Temp1, len);
                        Temp1 += len; i += len;
                        if (Temp1 != 4) break;
                        Temp1 = 0;
                        State = FrameParserState.Payload;
                        break;
                    case FrameParserState.Payload:
                        len = Math.Min(Incoming.PayloadLength - Temp1, dataLen - i);
                        PrimitiveBuffer.Copy(data, i, Incoming.Payload, Temp1, len);
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

        public override bool End()
        {
            if (!base.End()) return false;
            FrameQueue.End();
            return true;
        }
    }
}
