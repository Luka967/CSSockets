using CSSockets.Streams;
using System.Security.Cryptography;

namespace CSSockets.WebSockets
{
    // OPCODES
    // Continuation = 0
    // Text = 1
    // Binary = 2
    // Close = 8
    // Ping = 9
    // Pong = 10
    public class Frame
    {
        private static RandomNumberGenerator Rng { get; } = Rng = RandomNumberGenerator.Create();

        public byte Opcode { get; set; }
        public byte[] Mask { get; set; }
        public byte[] Payload { get; set; }
        public bool RSV1 { get; set; }
        public bool RSV2 { get; set; }
        public bool RSV3 { get; set; }
        public bool FIN { get; set; }
        public bool Masked => Mask != null;
        public ulong PayloadLength => (ulong)Payload.LongLength;

        public Frame() { }
        public Frame(bool fin, byte opcode, bool masked, byte[] payload, bool rsv1 = false, bool rsv2 = false, bool rsv3 = false)
        {
            FIN = fin;
            Opcode = opcode;
            Payload = payload;
            RSV1 = rsv1;
            RSV2 = rsv2;
            RSV3 = rsv3;
            if (!masked) return;
            Mask = new byte[4];
            Rng.GetBytes(Mask);
        }

        public void Serialize(IWritable to)
        {
            int payloadSerLen = PayloadLength >= 65536 ? 8 : PayloadLength >= 126 ? 2 : 0;
            WriteByte(to, (byte)(Opcode + (FIN ? 128 : 0) + (RSV1 ? 64 : 0) + (RSV2 ? 32 : 0) + (RSV3 ? 16 : 0)));
            switch (payloadSerLen)
            {
                case 0:
                    WriteByte(to, (byte)((Masked ? 128u : 0u) + PayloadLength));
                    break;
                case 2:
                    WriteByte(to, (byte)((Masked ? 128 : 0) + 126));
                    WriteByte(to, (byte)((PayloadLength >> 8) & 255));
                    WriteByte(to, (byte)(PayloadLength & 255));
                    break;
                case 8:
                    WriteByte(to, (byte)((Masked ? 128 : 0) + 127));
                    WriteByte(to, (byte)((PayloadLength >> 56) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 48) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 40) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 32) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 24) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 16) & 255));
                    WriteByte(to, (byte)((PayloadLength >> 8) & 255));
                    WriteByte(to, (byte)(PayloadLength & 255));
                    break;
            }
            if (Masked)
            {
                to.Write(Mask);
                FlipMask();
                to.Write(Payload);
            }
            else to.Write(Payload);
        }

        private static void WriteByte(IWritable to, byte a) => to.Write(new byte[] { a });

        internal void FlipMask()
        {
            for (ulong i = 0; i < PayloadLength; i++) Payload[i] = (byte)(Payload[i] ^ Mask[i & 3]);
        }
    }
}
