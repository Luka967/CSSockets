using System;
using CSSockets.Base;
using CSSockets.Streams;

namespace CSSockets.WebSockets
{
    public enum FrameMergeResponse : byte
    {
        Valid = 0,
        ContinuationOnNoOpcode = 1,
        OpcodeOnNonFin = 2
    }
    public class FrameMerger : IQueueableAsyncOutputter<Message>, IEndable
    {
        private byte Opcode { get; set; } = 0;
        private Queue<byte[]> DataQueue { get; } = new Queue<byte[]>();
        private long DataLength { get; set; } = 0;
        private object MergeLock { get; } = new object();

        private Queue<Message> MessageQueue { get; } = new Queue<Message>();
        public int QueuedCount => MessageQueue.Count;
        public event AsyncCreationHandler<Message> OnOutput;

        public bool Ended { get; private set; } = false;
        protected void ThrowIfEnded() { if (Ended) throw new ObjectDisposedException("This stream has already ended.", innerException: null); }

        public FrameMergeResponse MergeFrame(Frame frame)
        {
            ThrowIfEnded();
            lock (MergeLock)
            {
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
            ThrowIfEnded();
            if (!MessageQueue.Dequeue(out Message next))
                // ended
                return null;
            return next;
        }

        private void Deflate()
        {
            byte[] merged = new byte[DataLength];
            for (long i = 0; i < DataLength;)
            {
                DataQueue.Dequeue(out byte[] next);
                Frame.ArrayCopy(next, 0, merged, i, next.LongLength);
                i += next.LongLength;
            }
            Message m = new Message(Opcode, merged);
            if (OnOutput != null) OnOutput(m);
            else MessageQueue.Enqueue(m);
            DataLength = 0; Opcode = 0;
        }

        public void End()
        {
            ThrowIfEnded();
            lock (MergeLock)
            {
                DataQueue.End();
                MessageQueue.End();
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
