using System;
using CSSockets.Streams;
using CSSockets.Http.Base;

namespace CSSockets.WebSockets
{
    public enum FrameMergeResponse : byte
    {
        Valid = 0,
        ContinuationOnNoOpcode = 1,
        OpcodeOnNonFin = 2
    }
    public class FrameMerger : IQueueingOutputter<Message>, IEndable
    {
        private byte Opcode { get; set; } = 0;
        private readonly Queue<byte[]> DataQueue = new Queue<byte[]>();
        private ulong DataLength { get; set; } = 0;
        private object MergeLock { get; } = new object();

        private readonly Queue<Message> MessageQueue = new Queue<Message>();
        public int Queued => MessageQueue.Count;
        public event OutputterHandler<Message> OnOutput;
        public event ControlHandler OnEnd;

        public bool Ended { get; private set; } = false;
        protected void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null); }

        public FrameMergeResponse MergeFrame(Frame frame)
        {
            lock (MergeLock)
            {
                ThrowIfEnded();
                if (Opcode == 0 && frame.Opcode == 0)
                    return FrameMergeResponse.ContinuationOnNoOpcode;
                if (Opcode != 0 && frame.Opcode != 0)
                    return FrameMergeResponse.OpcodeOnNonFin;
                if (frame.Opcode != 0)
                    Opcode = frame.Opcode;
                DataQueue.Enqueue(frame.Payload);
                DataLength += frame.PayloadLength;
                if (frame.FIN) Deflate();
            }
            return FrameMergeResponse.Valid;
        }

        public Message Next()
        {
            if (Ended) return null;
            if (!MessageQueue.Dequeue(out Message next))
                // ended
                return null;
            return next;
        }

        private void Deflate()
        {
            byte[] merged = new byte[DataLength];
            for (ulong i = 0; i < DataLength;)
            {
                DataQueue.Dequeue(out byte[] next);
                PrimitiveBuffer.Copy(next, 0, merged, i, (ulong)next.LongLength);
                i += (ulong)next.LongLength;
            }
            Message m = new Message(Opcode, merged);
            if (OnOutput != null) OnOutput(m);
            else MessageQueue.Enqueue(m);
            DataLength = 0; Opcode = 0;
        }

        public bool End()
        {
            lock (MergeLock)
            {
                if (Ended) return false;
                Ended = true;
                DataQueue.End();
                MessageQueue.End();
                OnEnd?.Invoke();
                return true;
            }
        }
    }

    public class Message
    {
        public byte Opcode { get; }
        public byte[] Data { get; }

        internal Message(byte opcode, byte[] data)
        {
            Opcode = opcode;
            Data = data;
        }
    }
}
