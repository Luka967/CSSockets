using System;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace CSSockets.Streams
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
    public class StreamReader
    {
        public IReadable Stream { get; }

        public StreamReader(IReadable readable) => Stream = readable;
        public StreamReader(byte[] data)
        {
            MemoryDuplex duplex = new MemoryDuplex();
            duplex.Write(data);
            Stream = duplex;   
        }

        private byte[] NonBlockingUnsafeRead(ulong length)
        {
            byte[] data = new byte[length];
            ulong read = Stream.Read(data);
            if (read != length) return null;
            return data;
        }
        private byte[] NonBlockingRead(ulong length)
        {
            byte[] data = new byte[length];
            ulong read = Stream.Read(data);
            if (read != length) throw new IndexOutOfRangeException("Reached end of stream");
            return data;
        }

        public byte ReadUInt8() => Stream.Read(1)[0];
        public sbyte ReadInt8() => (sbyte)Stream.Read(1)[0];

        public ushort ReadUInt16BE() => (ushort)BinaryConversion.GetBE(NonBlockingRead(2));
        public ushort ReadUInt16LE() => (ushort)BinaryConversion.GetLE(NonBlockingRead(2));
        public short ReadInt16BE() => (short)BinaryConversion.GetBE(NonBlockingRead(2));
        public short ReadInt16LE() => (short)BinaryConversion.GetLE(NonBlockingRead(2));

        public uint ReadUInt32BE() => (uint)BinaryConversion.GetBE(NonBlockingRead(4));
        public uint ReadUInt32LE() => (uint)BinaryConversion.GetLE(NonBlockingRead(4));
        public int ReadInt32BE() => (int)BinaryConversion.GetBE(NonBlockingRead(4));
        public int ReadInt32LE() => (int)BinaryConversion.GetLE(NonBlockingRead(4));

        public ulong ReadUInt64BE() => BinaryConversion.GetBE(NonBlockingRead(8));
        public ulong ReadUInt64LE() => BinaryConversion.GetLE(NonBlockingRead(8));
        public long ReadInt64BE() => (long)BinaryConversion.GetBE(NonBlockingRead(8));
        public long ReadInt64LE() => (long)BinaryConversion.GetLE(NonBlockingRead(8));

        public ulong ReadUIntBE(byte size) => BinaryConversion.GetBE(NonBlockingRead(size / 8u));
        public ulong ReadUIntLE(byte size) => BinaryConversion.GetLE(NonBlockingRead(size / 8u));
        public long ReadIntBE(byte size) => BinaryConversion.ToSignedBE(BinaryConversion.GetBE(NonBlockingRead(size / 8u)), size);
        public long ReadIntLE(byte size) => BinaryConversion.ToSignedLE(BinaryConversion.GetLE(NonBlockingRead(size / 8u)), size);

        public float ReadFloat32BE() => new BinaryConversion.IntFloat32(ReadInt32BE()).Float;
        public float ReadFloat32LE() => new BinaryConversion.IntFloat32(ReadInt32LE()).Float;
        public double ReadFloat64BE() => new BinaryConversion.IntFloat64(ReadInt64BE()).Float;
        public double ReadFloat64LE() => new BinaryConversion.IntFloat64(ReadInt64LE()).Float;

        public string ReadString(Encoding encoding, ulong length)
            => encoding.GetString(NonBlockingRead(length));

        public string ReadStringUnicodeBEZT() => ReadStringZT(Encoding.BigEndianUnicode, 16);
        public string ReadStringUnicodeLEZT() => ReadStringZT(Encoding.Unicode, 16);
        public string ReadStringUTF8ZT() => ReadStringZT(Encoding.UTF8, 8);
        public string ReadStringZT(Encoding encoding, byte charSize)
        {
            PrimitiveBuffer buffer = new PrimitiveBuffer();
            while (true)
            {
                if (Stream.Ended) break;
                byte[] read = NonBlockingUnsafeRead(charSize / 8u);
                if (read == null || read.All((v) => v == 0)) break;
                buffer.Write(read);
            }
            return encoding.GetString(buffer.Read(buffer.Length));
        }
    }

    public class StreamWriter
    {
        public IWritable Stream { get; }

        public StreamWriter(IWritable writable) => Stream = writable;

        public bool WriteUInt8(byte value) => Stream.Write(new byte[] { value });
        public bool WriteInt8(sbyte value) => Stream.Write(new byte[] { (byte)value });

        public bool WriteUInt16BE(ushort value) => Stream.Write(BinaryConversion.SerializeBE(value, 2));
        public bool WriteUInt16LE(ushort value) => Stream.Write(BinaryConversion.SerializeLE(value, 2));
        public bool WriteInt16BE(short value) => Stream.Write(BinaryConversion.SerializeBE((ushort)value, 2));
        public bool WriteInt16LE(short value) => Stream.Write(BinaryConversion.SerializeLE((ushort)value, 2));

        public bool WriteUInt32BE(uint value) => Stream.Write(BinaryConversion.SerializeBE(value, 4));
        public bool WriteUInt32LE(uint value) => Stream.Write(BinaryConversion.SerializeLE(value, 4));
        public bool WriteInt32BE(int value) => Stream.Write(BinaryConversion.SerializeBE((uint)value, 4));
        public bool WriteInt32LE(int value) => Stream.Write(BinaryConversion.SerializeLE((uint)value, 4));

        public bool WriteUInt64BE(ulong value) => Stream.Write(BinaryConversion.SerializeBE(value, 8));
        public bool WriteUInt64LE(ulong value) => Stream.Write(BinaryConversion.SerializeLE(value, 8));
        public bool WriteInt64BE(long value) => Stream.Write(BinaryConversion.SerializeBE((ulong)value, 8));
        public bool WriteInt64LE(long value) => Stream.Write(BinaryConversion.SerializeLE((ulong)value, 8));

        public bool WriteUIntBE(ulong value, byte size) => Stream.Write(BinaryConversion.SerializeBE(value, size / 8));
        public bool WriteUIntLE(ulong value, byte size) => Stream.Write(BinaryConversion.SerializeLE(value, size / 8));
        public bool WriteIntBE(long value, byte size) => Stream.Write(BinaryConversion.SerializeBE((ulong)value, size / 8));
        public bool WriteIntLE(long value, byte size) => Stream.Write(BinaryConversion.SerializeLE((ulong)value, size / 8));

        public bool WriteFloat32BE(float value) => Stream.Write(BinaryConversion.SerializeBE((ulong)new BinaryConversion.IntFloat32(value).Integer, 4));
        public bool WriteFloat32LE(float value) => Stream.Write(BinaryConversion.SerializeLE((ulong)new BinaryConversion.IntFloat32(value).Integer, 4));
        public bool WriteFloat64BE(double value) => Stream.Write(BinaryConversion.SerializeBE((ulong)new BinaryConversion.IntFloat64(value).Integer, 8));
        public bool WriteFloat64LE(double value) => Stream.Write(BinaryConversion.SerializeLE((ulong)new BinaryConversion.IntFloat64(value).Integer, 8));

        public bool WriteStringUTF8(string value) => WriteString(value, Encoding.UTF8);
        public bool WriteStringUnicodeBE(string value) => WriteString(value, Encoding.BigEndianUnicode);
        public bool WriteStringUnicodeLE(string value) => WriteString(value, Encoding.Unicode);
        public bool WriteString(string value, Encoding encoding)
        {
            byte[] data = encoding.GetBytes(value);
            return Stream.Write(data);
        }
    }
}
