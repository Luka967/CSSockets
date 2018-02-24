using System;
using System.Text;
using CSSockets.Streams;
using System.IO.Compression;

namespace CSSockets.Http.Reference
{
    sealed public class BodySerializer : UnifiedDuplex
    {
        private const string CRLF = "\r\n";
        private static readonly byte[] CRLF_BYTES = Encoding.ASCII.GetBytes(CRLF);
        private static readonly byte[] LAST_CHUNK = Encoding.ASCII.GetBytes("0" + CRLF + CRLF);

        private UnifiedDuplex transform = null;
        private BodyType? type = null;

        public ulong? ContentLength { get; private set; } = null;
        public ulong? CurrentContentLength { get; private set; } = null;
        public CompressionType CompressionType => type?.CompressionType ?? throw new InvalidOperationException("Not set");
        public TransferEncoding TransferEncoding => type?.TransferEncoding ?? throw new InvalidOperationException("Not set");
        public event ControlHandler OnFinish;

        public bool TrySetFor(BodyType detected, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            lock (Wlock)
            {
                if (Ended) return false;

                CompressionType compression = detected.CompressionType;
                TransferEncoding transfer = detected.TransferEncoding;
                ulong? contentLength = detected.ContentLength;

                if (type != null) Reset();
                if (transfer == TransferEncoding.Raw)
                    ContentLength = detected.ContentLength;
                type = detected;
                CurrentContentLength = 0;
                return transfer == TransferEncoding.None ? true : SetCompression(compression);
            }
        }
        public bool SetCompression(CompressionType compression, CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            lock (Wlock)
            {
                if (!type.HasValue) return false;
                if (CurrentContentLength > 0) return false;
                if (transform != null) transform.End();
                if (type.Value.TransferEncoding != TransferEncoding.None)
                    switch (compression)
                    {
                        case CompressionType.None: transform = new MemoryDuplex(); break;
                        case CompressionType.Gzip: transform = new GzipCompressor(compressionLevel); break;
                        case CompressionType.Deflate: transform = new DeflateCompressor(compressionLevel); break;
                        default: return false;
                    }
                else return false;
                type = new BodyType(type.Value.ContentLength, type.Value.TransferEncoding, compression);
                return true;
            }
        }

        private bool WriteTransformed(byte[] source)
        {
            switch (TransferEncoding)
            {
                case TransferEncoding.None: return false;
                case TransferEncoding.Raw:
                    if (source == null) return true;
                    ulong len = (ulong)source.LongLength;
                    if (CurrentContentLength + len > ContentLength) return false;
                    CurrentContentLength += len;
                    Bhandle(source);
                    if (CurrentContentLength == ContentLength) return Finish();
                    break;
                case TransferEncoding.Chunked:
                    if (source != null)
                    {
                        string str = source.Length.ToString("X");
                        Bhandle(Encoding.ASCII.GetBytes(str));
                        Bhandle(CRLF_BYTES);
                        Bhandle(source);
                        Bhandle(CRLF_BYTES);
                        CurrentContentLength += (ulong)source.Length + 2 + (ulong)source.LongLength + 2;
                    }
                    else
                    {
                        // last chunk
                        Bhandle(LAST_CHUNK);
                        ContentLength = CurrentContentLength += 5;
                        return Finish();
                    }
                    break;
            }
            return false;
        }
        public override bool Write(byte[] source)
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (type == null) return false;
                if (type.Value.TransferEncoding == TransferEncoding.None)
                    return false;
                if (source == null) return true;

                transform.Write(source);
                if (transform.Buffered > 0)
                    return WriteTransformed(transform.Read());
                return true;
            }
        }
        public override bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));
        public override byte[] Read() => Bread();
        public override byte[] Read(ulong length) => Bread(length);
        public override ulong Read(byte[] destination) => Bread(destination);

        public bool Reset()
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (type == null) return false;
                if (TransferEncoding != TransferEncoding.None && !transform.End()) return false;
                ContentLength = CurrentContentLength = null;
                transform = null;
                type = null;
                return true;
            }
        }
        public bool Finish()
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (type == null) return false;
                if (TransferEncoding != TransferEncoding.None)
                {
                    if (CompressionType != CompressionType.None && CompressionType != CompressionType.Unknown)
                    {
                        CompressorDuplex compressor = transform as CompressorDuplex;
                        compressor.Finish();
                        if (compressor.Buffered > 0) WriteTransformed(compressor.Read());
                    }
                    WriteTransformed(null);
                }
                OnFinish?.Invoke();
                return Reset();
            }
        }
        public override bool End()
        {
            lock (Wlock)
            {
                if (type != null && !Finish()) return false;
                return base.End();
            }
        }
    }
}
