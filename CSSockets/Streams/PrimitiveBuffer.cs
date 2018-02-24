using System;
using System.IO;
using System.Security;
using System.Runtime.InteropServices;

namespace CSSockets.Streams
{
    public class PrimitiveBuffer
    {
        /// <summary>
        /// Capacity modifier multiplier
        /// </summary>
        public const ulong CMM = 256;

        public byte[] Buffer = new byte[CMM];
        public ulong Length { get; private set; } = 0;
        public ulong Capacity { get; private set; } = CMM;

        private unsafe void EnsureCapacity(ulong targetLen)
        {
            ulong beforeLen = Length;
            Length = targetLen;
            ulong targetCap = Capacity;
            if (targetCap > targetLen) while (targetCap / CMM >= CMM && targetCap / CMM > targetLen) targetCap /= CMM;
            else while (targetCap < targetLen) targetCap *= CMM;
            if (targetCap == Capacity) return;
            byte[] newBuf = new byte[targetCap];
            fixed (byte* src = Buffer, dst = newBuf) Copy(src, 0, dst, 0, Math.Min(beforeLen, targetLen));
            Buffer = newBuf;
            Capacity = targetCap;
        }
        public unsafe byte[] Read(ulong length) => Read(new byte[length]);
        public unsafe byte[] Read(byte[] ret)
        {
            if (ret == null) throw new ArgumentNullException(nameof(ret));
            ulong length = (ulong)ret.LongLength;
            if (length > Length) throw new ArgumentOutOfRangeException(nameof(length));
            fixed (byte* src = Buffer, dst = ret)
            {
                Copy(src, 0, dst, 0, length);
                ulong remaining = Length - length;
                Copy(src, length, src, 0, remaining);
                EnsureCapacity(remaining);
            }
            return ret;
        }
        public unsafe void Write(byte[] data)
        {
            ulong len = (ulong)data.LongLength;
            ulong start = Length;
            ulong end = Length + len;
            EnsureCapacity(end);
            fixed (byte* src = data, dst = Buffer)
                Copy(src, 0, dst, start, len);
        }

        // windows
        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false), SuppressUnmanagedCodeSecurity]
        private static unsafe extern void* WMEMCPY(void* dest, void* src, ulong count);
        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false), SuppressUnmanagedCodeSecurity]
        private static unsafe extern void* WMEMCPY(void* dest, void* src, int count);

        // *nix
        [DllImport("libc", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl), SuppressUnmanagedCodeSecurity]
        private static unsafe extern void* UMEMCPY(void* dest, void* src, ulong count);
        [DllImport("libc", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl), SuppressUnmanagedCodeSecurity]
        private static unsafe extern void* UMEMCPY(void* dest, void* src, int count);

        public static bool IS_WINDOWS = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static unsafe void* MEMCPY(void* dest, void* src, ulong count)
            => IS_WINDOWS ? WMEMCPY(dest, src, count) : UMEMCPY(dest, src, count);
        private static unsafe void* MEMCPY(void* dest, void* src, int count)
            => IS_WINDOWS ? WMEMCPY(dest, src, count) : UMEMCPY(dest, src, count);

        public static unsafe byte[] Slice(byte[] source, ulong start, ulong end)
        {
            byte[] srcChunked = new byte[end - start];
            fixed (byte* src = source, dst = srcChunked)
                MEMCPY(dst + start, src, end - start);
            return srcChunked;
        }
        public static unsafe byte[] Slice(byte[] source, int start, int end)
        {
            byte[] srcChunked = new byte[end - start];
            fixed (byte* src = source, dst = srcChunked)
                MEMCPY(dst + start, src, end - start);
            return srcChunked;
        }
        public static unsafe void Copy(byte* src, ulong srcStart, byte* dst, ulong dstStart, ulong length)
            => MEMCPY(dst + dstStart, src + srcStart, length);
        public static unsafe void Copy(byte[] src, ulong srcStart, byte[] dst, ulong dstStart, ulong length)
        {
            fixed (byte* Src = src, Dst = dst)
                MEMCPY(Dst + dstStart, Src + srcStart, length);
        }
        public static unsafe void Copy(byte[] src, int srcStart, byte[] dst, int dstStart, int length)
        {
            fixed (byte* Src = src, Dst = dst)
                MEMCPY(Dst + dstStart, Src + srcStart, length);
        }
    }

    public class MemoryStream : Stream
    {
        public readonly PrimitiveBuffer Pbuffer = new PrimitiveBuffer();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => (long)Pbuffer.Length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ulong len = Math.Min(Pbuffer.Length, (ulong)count);
            byte[] buf = Pbuffer.Read(len);
            if (buf.Length == 0) return 0;
            PrimitiveBuffer.Copy(buf, 0, buffer, offset, count);
            return (int)len;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count)
            => Pbuffer.Write(PrimitiveBuffer.Slice(buffer, offset, offset + count));
    }
}
