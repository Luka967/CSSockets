using CSSockets.Binary;
using CSSockets.Streams;
using System.Security.Cryptography;

namespace CSSockets.WebSockets.Definition
{
    public struct Frame
    {
        private static RandomNumberGenerator rng = RandomNumberGenerator.Create();

        public static byte GetXLengthSize(byte length)
            => length == 127 ? (byte)8 : length == 126 ? (byte)2 : (byte)0;
        public static byte GetLengthFromXLength(ulong xLength)
            => xLength >= 65536 ? (byte)127 : xLength >= 126 ? (byte)126 : (byte)xLength;

        public bool FIN { get; set; }
        public byte Opcode { get; set; }
        public bool RSV1 { get; set; }
        public bool RSV2 { get; set; }
        public bool RSV3 { get; set; }
        public byte Length { get; set; }
        public ulong ExtendedLength { get; set; }
        public bool Masked { get; set; }
        public byte[] Mask { get; set; }
        public byte[] Payload { get; set; }

        public Frame(byte head1, byte head2) : this()
        {
            FIN = (head1 & 128) == 128;
            RSV1 = (head1 & 64) == 64;
            RSV2 = (head1 & 32) == 32;
            RSV3 = (head1 & 16) == 16;
            Opcode = (byte)(head1 - (FIN ? 128 : 0) - (RSV1 ? 64 : 0) - (RSV2 ? 32 : 0) - (RSV3 ? 16 : 0));
            Masked = (head2 & 128) == 128;
            Length = (byte)(head2 - (Masked ? 128 : 0));
            Payload = null;
        }
        public Frame(bool fin, byte opcode, bool rsv1, bool rsv2, bool rsv3, bool masked, byte[] payload) : this()
        {
            FIN = fin;
            Opcode = opcode;
            RSV1 = rsv1;
            RSV2 = rsv2;
            RSV3 = rsv3;
            ExtendedLength = (ulong)payload.LongLength;
            Length = GetLengthFromXLength(ExtendedLength);
            if (Masked = masked)
            {
                Mask = new byte[4];
                rng.GetBytes(Mask);
            }
            Payload = payload;
        }
        public Frame(bool fin, byte opcode, bool rsv1, bool rsv2, bool rsv3, byte[] mask, byte[] payload) : this()
        {
            FIN = fin;
            Opcode = opcode;
            RSV1 = rsv1;
            RSV2 = rsv2;
            RSV3 = rsv3;
            ExtendedLength = (ulong)payload.LongLength;
            Length = GetLengthFromXLength(ExtendedLength);
            Masked = true;
            Mask = mask;
            Payload = payload;
        }

        public void SerializeTo(IWritable writable)
        {
            StreamWriter writer = new StreamWriter(writable);
            writer.WriteUInt8((byte)(Opcode + (FIN ? 128 : 0) + (RSV1 ? 64 : 0) + (RSV2 ? 32 : 0) + (RSV3 ? 16 : 0)));
            writer.WriteUInt8((byte)(Length + (Mask != null ? 128 : 0)));
            switch (GetXLengthSize(Length))
            {
                case 0: break;
                case 2: writer.WriteUInt16LE((ushort)ExtendedLength); break;
                case 8: writer.WriteUInt64LE(ExtendedLength); break;
            }
            if (Mask != null) { writable.Write(Mask); writable.Write(FlipMask(Payload, Mask)); }
            else writable.Write(Payload);
        }

        public static byte[] FlipMask(byte[] payload, byte[] mask)
        {
            byte[] copied = PrimitiveBuffer.Slice(payload, 0, (ulong)payload.LongLength);
            for (long i = 0; i < copied.LongLength; i++) copied[i] ^= mask[i & 3];
            return copied;
        }
    }

    public struct Message
    {
        public byte Opcode { get; }
        public byte[] Payload { get; }
        public ulong Length { get; }

        public Message(byte opcode, byte[] payload, ulong length) : this()
        {
            Opcode = opcode;
            Payload = payload;
            Length = length;
        }
    }

    public sealed class FrameParser : Collector<Frame>
    {
        enum ParseState : byte
        {
            Head,
            ExtendedLen2,
            ExtendedLen8,
            Mask,
            Payload
        }

        private Frame Incoming = new Frame();
        private readonly MemoryDuplex Buffer;
        private readonly StreamReader Reader;
        private ParseState State = ParseState.Head;

        public FrameParser() => Reader = new StreamReader(Buffer = new MemoryDuplex());

        protected override bool HandleWritable(byte[] source)
        {
            Buffer.Write(source); ulong buffered;
            while ((buffered = Buffer.BufferedReadable) > 0)
                switch (State)
                {
                    case ParseState.Head:
                        if (buffered < 2) return true;
                        Incoming = new Frame(Reader.ReadUInt8(), Reader.ReadUInt8());
                        switch (Frame.GetXLengthSize(Incoming.Length))
                        {
                            case 0:
                                Incoming.ExtendedLength = Incoming.Length;
                                if (Incoming.Masked) State = ParseState.Mask;
                                else if (Incoming.Length > 0) State = ParseState.Payload;
                                else Reset();
                                break;
                            case 2: State = ParseState.ExtendedLen2; break;
                            case 8: State = ParseState.ExtendedLen8; break;
                        }
                        break;
                    case ParseState.ExtendedLen2:
                        if (buffered < 2) return true;
                        Incoming.ExtendedLength = Reader.ReadUInt16LE();
                        if (Incoming.Masked) State = ParseState.Mask;
                        else if (Incoming.ExtendedLength > 0) State = ParseState.Payload;
                        else Reset();
                        break;
                    case ParseState.ExtendedLen8:
                        if (buffered < 8) return true;
                        Incoming.ExtendedLength = Reader.ReadUInt64LE();
                        if (Incoming.Masked) State = ParseState.Mask;
                        else if (Incoming.ExtendedLength > 0) State = ParseState.Payload;
                        else Reset();
                        break;
                    case ParseState.Mask:
                        if (buffered < 4) return true;
                        Incoming.Mask = Buffer.Read(4);
                        State = ParseState.Payload;
                        break;
                    case ParseState.Payload:
                        if (buffered < Incoming.ExtendedLength) return true;
                        Incoming.Payload = Buffer.Read(Incoming.ExtendedLength);
                        Reset();
                        break;
                }
            return true;
        }

        private bool Reset()
        {
            Incoming.Payload = Incoming.Payload ?? new byte[0];
            Pickup(Incoming);
            Incoming = new Frame();
            State = ParseState.Head;
            return true;
        }
    }

    public delegate void MessageHandler(Message message);
    public sealed class FrameMerger
    {
        public enum Response : byte
        {
            OK,
            Reset,
            MessageNotStarted,
            MessageNotFinished
        }
        private readonly object Sync = new object();

        public event MessageHandler OnCollect;
        private byte IncomingOpcode = 0;
        private readonly MemoryDuplex IncomingMessage = new MemoryDuplex();

        public Response Push(Frame frame)
        {
            lock (Sync)
            {
                if (frame.Opcode == 0 && IncomingOpcode == 0)
                    return Response.MessageNotStarted;
                if (frame.Opcode != 0 && IncomingOpcode != 0)
                    return Response.MessageNotFinished;
                if (frame.Opcode != 0) IncomingOpcode = frame.Opcode;
                IncomingMessage.Write(frame.Masked ? Frame.FlipMask(frame.Payload, frame.Mask) : frame.Payload);
                if (!frame.FIN) return Response.OK;
                ulong length = IncomingMessage.BufferedReadable;
                OnCollect?.Invoke(new Message(IncomingOpcode, IncomingMessage.Read(), length));
                IncomingOpcode = 0;
                return Response.Reset;
            }
        }
    }
}
