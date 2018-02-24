using System;
using CSSockets.Streams;
using CSSockets.Http.Structures;
using System.Globalization;

namespace CSSockets.Http.Reference
{
    sealed public class BodyParser : UnifiedDuplex
    {
        private enum ParserState : sbyte
        {
            Dormant = -1,
            RawRead = 0,
            Chunked_Length = 1,
            Chunked_LenLf = 2,
            Chunked_ChunkData = 3,
            Chunked_ChunkCr = 4,
            Chunked_ChunkLf = 5,
            Chunked_Trailer = 6,
            Chunked_Lf = 7
        }
        private const char CR = '\r';
        private const char LF = '\n';

        private readonly MemoryDuplex excess = new MemoryDuplex();
        public IReadable Excess => excess;

        private UnifiedDuplex transform;
        private BodyType? type = null;
        private ParserState state = ParserState.Dormant;
        private StringQueue Squeue = null;
        private bool Malformed = false;
        private ulong? chunkLen = null;
        private ulong? chunkIndex = null;

        public ulong? ContentLength { get; private set; } = null;
        public ulong? CurrentContentLength { get; private set; } = null;
        public CompressionType CompressionType => type?.CompressionType ?? throw new InvalidOperationException("Not set");
        public TransferEncoding TransferEncoding => type?.TransferEncoding ?? throw new InvalidOperationException("Not set");
        public event ControlHandler OnFinish;

        public bool TrySetFor(BodyType detected)
        {
            lock (Wlock)
            {
                if (Ended) return false;

                CompressionType compression = detected.CompressionType;
                TransferEncoding transfer = detected.TransferEncoding;
                ulong? contentLength = detected.ContentLength;

                if (type != null) Reset();
                if (transfer == TransferEncoding.Raw)
                {
                    ContentLength = detected.ContentLength;
                    state = ParserState.RawRead;
                }
                else if (transfer == TransferEncoding.Chunked)
                {
                    state = ParserState.Chunked_Length;
                    Squeue = new StringQueue();
                    chunkIndex = chunkLen = 0;
                }
                type = detected;
                CurrentContentLength = 0;
                return transfer == TransferEncoding.None ? true : SetCompression(compression);
            }
        }
        public bool SetCompression(CompressionType compression)
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
                        case CompressionType.Gzip: transform = new GzipDecompressor(); break;
                        case CompressionType.Deflate: transform = new DeflateDecompressor(); break;
                        default: return false;
                    }
                else return false;
                type = new BodyType(type.Value.ContentLength, type.Value.TransferEncoding, compression);
                return true;
            }
        }

        public override bool Write(byte[] source)
        {
            lock (Wlock)
            {
                ThrowIfEnded();
                if (!type.HasValue) return false;
                ulong i = 0, sourceLen = (ulong)source.LongLength, len; char c;
                for (; i < sourceLen;)
                {
                    switch (state)
                    {
                        case ParserState.RawRead:
                            len = ContentLength == null ? sourceLen - i : Math.Min(sourceLen - i, ContentLength.Value - CurrentContentLength.Value);
                            if (!transform.Write(source, i, i + len))
                                return (Malformed = true) && !End();
                            i += len; CurrentContentLength += len;
                            if (transform.Buffered > 0) Bhandle(transform.Read());
                            if (ContentLength == null || CurrentContentLength < ContentLength) break;
                            state = ParserState.Dormant;
                            Reset();
                            break;
                        case ParserState.Chunked_Length:
                            c = (char)source[i++];
                            if (c != CR) Squeue.Append(c);
                            else
                            {
                                if (!ulong.TryParse(Squeue.Next(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong result))
                                    return (Malformed = true) && !End();
                                chunkLen = result;
                                state = ParserState.Chunked_LenLf;
                            }
                            break;
                        case ParserState.Chunked_LenLf:
                            c = (char)source[i++];
                            if (c != LF) return (Malformed = true) && !End();
                            if (chunkLen == 0) state = ParserState.Chunked_Trailer;
                            else state = ParserState.Chunked_ChunkData;
                            break;
                        case ParserState.Chunked_ChunkData:
                            len = Math.Min(sourceLen - i, chunkLen.Value - chunkIndex.Value);
                            if (!transform.Write(source, i, i + len))
                                return (Malformed = true) && !End();
                            i += len; CurrentContentLength += len; chunkIndex += len;
                            if (transform.Buffered > 0) Bhandle(transform.Read());
                            if (chunkLen == chunkIndex)
                                state = ParserState.Chunked_ChunkCr;
                            break;
                        case ParserState.Chunked_ChunkCr:
                            c = (char)source[i++];
                            if (c != CR) return (Malformed = true) && !End();
                            state = ParserState.Chunked_ChunkLf;
                            break;
                        case ParserState.Chunked_ChunkLf:
                            c = (char)source[i++];
                            if (c != LF) return (Malformed = true) && !End();
                            state = ParserState.Chunked_Length;
                            chunkLen = chunkIndex = 0;
                            Squeue.New();
                            break;
                        case ParserState.Chunked_Trailer:
                            c = (char)source[i++];
                            if (c != CR) chunkIndex++;
                            else
                            {
                                if (chunkIndex == 0) state = ParserState.Chunked_Lf;
                                else chunkIndex = null; // LF will be added
                            }
                            break;
                        case ParserState.Chunked_Lf:
                            c = (char)source[i++];
                            if (c != LF) return (Malformed = true) && !End();
                            state = ParserState.Dormant;
                            Reset();
                            break;
                        default: return false;
                    }
                }
                excess.Write(source, i, sourceLen);
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
                if (type == null) return false;
                if (type.Value.TransferEncoding != TransferEncoding.None && !transform.End()) return false;
                ContentLength = CurrentContentLength = null;
                chunkLen = chunkIndex = null;
                state = ParserState.Dormant;
                transform = null;
                type = null;
                OnFinish?.Invoke();
                return true;
            }
        }
        public override bool End()
        {
            lock (Rlock)
                lock (Wlock)
                {
                    if (base.End())
                    {
                        if (Malformed) FireFail();
                        if (type != null && !Reset()) return false;
                        return excess.End();
                    }
                    return false;
                }
        }
    }
}
