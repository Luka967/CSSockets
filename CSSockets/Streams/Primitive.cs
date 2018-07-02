using System;

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

        private unsafe bool EnsureCapacity(ulong targetLen)
        {
            ulong beforeLen = Length;
            Length = targetLen;
            ulong targetCap = Capacity;
            if (targetCap > targetLen) while (targetCap / CMM >= CMM && targetCap / CMM > targetLen) targetCap /= CMM;
            else while (targetCap < targetLen) targetCap *= CMM;
            if (targetCap == Capacity) return false;
            byte[] newBuf = new byte[targetCap];
            fixed (byte* src = Buffer, dst = newBuf) Copy(src, 0, dst, 0, Math.Min(beforeLen, targetLen));
            Buffer = newBuf;
            Capacity = targetCap;
            return true;
        }
        public unsafe byte[] Read(ulong length) => Read(new byte[length]);
        public unsafe byte[] Read(byte[] ret) => Read(ret, (ulong)ret.LongLength);
        public unsafe byte[] Read(byte[] ret, ulong length, ulong start = 0)
        {
            if (ret == null) throw new ArgumentNullException(nameof(ret));
            if (length > Length) throw new ArgumentOutOfRangeException(nameof(length));
            fixed (byte* src = Buffer, dst = ret)
            {
                Copy(src, 0, dst + start, 0, length);
                ulong remaining = Length - length;
                Copy(src, length, src, 0, remaining);
                EnsureCapacity(remaining);
            }
            return ret;
        }
        public unsafe bool Write(byte[] data)
        {
            ulong len = (ulong)data.LongLength;
            ulong start = Length;
            ulong end = Length + len;
            EnsureCapacity(end);
            fixed (byte* src = data, dst = Buffer)
                Copy(src, 0, dst, start, len);
            return true;
        }

        private static unsafe void MEMCPY(void* dest, void* src, ulong count)
            => System.Buffer.MemoryCopy(src, dest, count, count);
        private static unsafe void MEMCPY(void* dest, void* src, int count)
            => System.Buffer.MemoryCopy(src, dest, count, count);

        public static unsafe byte[] Slice(byte[] source, ulong start, ulong end)
        {
            byte[] srcChunked = new byte[end - start];
            fixed (byte* src = source, dst = srcChunked)
                MEMCPY(dst, src + start, end - start);
            return srcChunked;
        }
        public static unsafe byte[] Slice(byte[] source, int start, int end)
        {
            byte[] srcChunked = new byte[end - start];
            fixed (byte* srcp = source, dstp = srcChunked)
                MEMCPY(dstp, srcp + start, end - start);
            return srcChunked;
        }
        public static unsafe void Copy(byte* src, ulong srcStart, byte* dst, ulong dstStart, ulong length)
            => MEMCPY(dst + dstStart, src + srcStart, length);
        public static unsafe void Copy(byte[] src, ulong srcStart, byte[] dst, ulong dstStart, ulong length)
        {
            fixed (byte* srcp = src, dstp = dst) MEMCPY(dstp + dstStart, srcp + srcStart, length);
        }
        public static unsafe void Copy(byte[] src, int srcStart, byte[] dst, int dstStart, int length)
        {
            fixed (byte* srcp = src, dstp = dst) MEMCPY(dstp + dstStart, srcp + srcStart, length);
        }
    }
}
