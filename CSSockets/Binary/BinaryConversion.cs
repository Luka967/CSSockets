using System;
using System.Runtime.InteropServices;

namespace CSSockets.Binary
{
    public static class BinaryConversion
    {
        public static ulong GetBE(byte[] data)
        {
            ulong value = 0;
            for (int i = 0; i < data.Length; i++) value = unchecked(value << 8 | (data[i] & 0xFFu));
            return value;
        }
        public static ulong GetLE(byte[] data)
        {
            ulong value = 0;
            for (int i = 0; i < data.Length; i++) value = unchecked(value << 8 | (data[data.Length - 1 - i] & 0xFFu));
            return value;
        }
        public static bool IsNegativeBE(ulong value, int size) => ((value >> (size - 8)) & 128) == 128;
        public static bool IsNegativeLE(ulong value, int size) => (value & 128) == 128;
        public static long ToSignedBE(ulong value, int size) => IsNegativeBE(value, size) ? -(long)Math.Pow(2, size) + (long)value : (long)value;
        public static long ToSignedLE(ulong value, int size) => IsNegativeLE(value, size) ? -(long)Math.Pow(2, size) + (long)value : (long)value;
        public static byte[] SerializeBE(ulong value, int size)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[size - 1 - i] = (byte)(value & 0xFF);
                value >>= 8;
            }
            return data;
        }
        public static byte[] SerializeLE(ulong value, int size)
        {
            byte[] data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)(value & 0xFF);
                value >>= 8;
            }
            return data;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct IntFloat32
        {
            [FieldOffset(0)]
            int i;
            [FieldOffset(0)]
            float f;

            public int Integer => i;
            public float Float => f;

            public IntFloat32(int i) : this()
            {
                f = default(float);
                this.i = i;
            }
            public IntFloat32(float f) : this()
            {
                i = default(int);
                this.f = f;
            }
        }
        [StructLayout(LayoutKind.Explicit)]
        public struct IntFloat64
        {
            [FieldOffset(0)]
            long i;
            [FieldOffset(0)]
            double f;

            public long Integer => i;
            public double Float => f;

            public IntFloat64(long i) : this()
            {
                f = default(double);
                this.i = i;
            }
            public IntFloat64(double f) : this()
            {
                i = default(long);
                this.f = f;
            }
        }
    }
}
