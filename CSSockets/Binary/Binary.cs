using System;
using System.Linq;
using System.Text;
using CSSockets.Streams;

namespace CSSockets.Binary
{
    public abstract class Reader
    {
        protected abstract byte[] NonBlockingRead(ulong length);
        protected abstract byte[] NonBlockingUnsafeRead(ulong length);

        public byte ReadUInt8() => NonBlockingRead(1)[0];
        public sbyte ReadInt8() => (sbyte)NonBlockingRead(1)[0];

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

        public string ReadString(Encoding encoding, ulong length, byte size) => encoding.GetString(NonBlockingRead(length * (size / 8u)));

        public string ReadStringUnicodeBEZT() => ReadStringZT(Encoding.BigEndianUnicode, 16);
        public string ReadStringUnicodeLEZT() => ReadStringZT(Encoding.Unicode, 16);
        public string ReadStringUTF8ZT() => ReadStringZT(Encoding.UTF8, 8);
        public string ReadStringZT(Encoding encoding, byte size)
        {
            PrimitiveBuffer buffer = new PrimitiveBuffer();
            ulong block = size / 8u;
            while (true)
            {
                byte[] read = NonBlockingUnsafeRead(block);
                if (read == null || read.All((v) => v == 0)) break;
                buffer.Write(read);
            }
            return encoding.GetString(buffer.Read(buffer.Length));
        }
    }
    public sealed class StreamReader : Reader
    {
        public IReadable Stream { get; }

        public StreamReader(IReadable readable) => Stream = readable;

        protected sealed override byte[] NonBlockingUnsafeRead(ulong length)
        {
            byte[] data = new byte[length];
            ulong read = Stream.Read(data);
            if (read != length) return null;
            return data;
        }
        protected sealed override byte[] NonBlockingRead(ulong length)
        {
            byte[] data = new byte[length];
            ulong read = Stream.Read(data);
            if (read != length) throw new IndexOutOfRangeException("Reached end of stream");
            return data;
        }
    }
    public sealed class MemoryReader : Reader
    {
        public byte[] Data { get; }
        private readonly object Sync = new object();
        private ulong size;
        private ulong offset = 0;

        public MemoryReader(byte[] data, ulong offset = 0)
        {
            Data = data;
            size = (ulong)data.LongLength;
            this.offset = offset;
        }

        protected sealed override byte[] NonBlockingUnsafeRead(ulong length)
        {
            lock (Sync)
            {
                if (offset + length > size) return null;
                return PrimitiveBuffer.Slice(Data, offset - length, offset += length);
            }
        }
        protected sealed override byte[] NonBlockingRead(ulong length)
        {
            lock (Sync)
            {
                if (offset + length > size) throw new IndexOutOfRangeException("Reached end of memory");
                return PrimitiveBuffer.Slice(Data, offset - length, offset += length);
            }
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
        public bool WriteString(string value, Encoding encoding) => Stream.Write(encoding.GetBytes(value));
    }
}
