using System;
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
        public long PayloadLength => Payload.Length;

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

        public byte[] Serialize()
        {
            int payloadSerLen = PayloadLength > 65535 ? 8 : PayloadLength > 126 ? 2 : 0;
            long len = 2 + payloadSerLen + (Masked ? 4 : 0) + PayloadLength, index = 0;
            byte[] ret = new byte[len];
            ret[index++] = (byte)(Opcode + (FIN ? 128 : 0) + (RSV1 ? 64 : 0) + (RSV2 ? 32 : 0) + (RSV3 ? 16 : 0));
            switch (payloadSerLen)
            {
                case 0:
                    ret[index++] = (byte)((Masked ? 128 : 0) + PayloadLength);
                    break;
                case 2:
                    ret[index++] = (byte)((Masked ? 128 : 0) + 126);
                    ArrayCopy(BitConverter.GetBytes((ushort)Payload.LongLength), 0, ret, index, 2);
                    index += 2;
                    break;
                case 8:
                    ret[index++] = (byte)((Masked ? 128 : 0) + 127);
                    ArrayCopy(BitConverter.GetBytes((ulong)Payload.LongLength), 0, ret, index, 8);
                    index += 8;
                    break;
            }
            if (Masked)
            {
                ArrayCopy(Mask, 0, ret, index, 4);
                index += 4;
                FlipMask();
                ArrayCopy(Payload, 0, ret, index, PayloadLength);
                FlipMask();
            }
            else ArrayCopy(Payload, 0, ret, index, PayloadLength);
            return ret;
        }

        internal void FlipMask()
        {
            for (long i = 0; i < PayloadLength; i++)
                Payload[i] = (byte)(Payload[i] ^ Mask[i & 3]);
        }

        internal static void ArrayCopy(byte[] src, long srcBegin, byte[] dst, long dstBegin, long length)
        {
            for (long a = srcBegin, b = dstBegin, c = 0; c < length; c++) dst[b++] = src[a++];
        }
    }
}
