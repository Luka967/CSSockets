using System;
using CSSockets.Streams;
using CSSockets.Http.Base;
using System.Globalization;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    internal enum BodyParserState : sbyte
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
    // readable side is content body, writable side is excess data if any
    sealed public class BodyParser : BaseDuplex
    {
        private const char CR = '\r';
        private const char LF = '\n';

        public UnifiedDuplex ContentTransform { get; private set; } = null;
        public TransferEncoding TransferEncoding { get; private set; } = TransferEncoding.None;
        public CompressionType CompressionType { get; private set; } = CompressionType.Unknown;
        public event ControlHandler OnEnd;

        public int ContentLength { get; set; } = -1; // -1 = unknown, 0 = none, 1+ = determined
        public int CurrentReadBytes { get; set; } = 0;
        private int chunkLen = 0, chunkIndex = 0;
        private StringQueue StringQueue { get; set; } = null;

        private bool IsSet { get; set; } = false;
        private BodyParserState State { get; set; } = BodyParserState.Dormant;

        public bool TrySetFor(MessageHead head)
        {
            ThrowIfEnded();
            BodyType? bodyType = BodyType.TryDetectFor(head);
            if (bodyType == null) return false;

            CompressionType compression = bodyType.Value.CompressionType;
            TransferEncoding transfer = bodyType.Value.TransferEncoding;
            int contentLength = bodyType.Value.ContentLength;

            if (IsSet) Reset();
            TransferEncoding = transfer;
            if (transfer == TransferEncoding.Raw)
            {
                ContentLength = contentLength;
                State = BodyParserState.RawRead;
            }
            else if (transfer == TransferEncoding.Chunked)
            {
                State = BodyParserState.Chunked_Length;
                StringQueue = new StringQueue();
            }
            if (transfer != TransferEncoding.None)
            {
                CompressionType = compression;
                switch (compression)
                {
                    case CompressionType.None: ContentTransform = new RawUnifiedDuplex(); break;
                    case CompressionType.Gzip: ContentTransform = new GzipDecompressor(); break;
                    case CompressionType.Deflate: ContentTransform = new DeflateDecompressor(); break;
                    case CompressionType.Compress: return false;
                    default: return false;
                }
            }
            IsSet = true;
            return true;
        }
        public void SetFor(MessageHead head)
        {
            if (!TrySetFor(head)) throw new ArgumentException("Could not figure out body encoding");
        }

        private int ProcessData(byte[] data, bool writeExcess)
        {
            ThrowIfEnded();
            if (!IsSet) throw new InvalidOperationException("Not set");
            int i = 0, len; char c; byte[] sliced;
            for (; i < data.Length;)
            {
                switch (State)
                {
                    case BodyParserState.RawRead:
                        len = ContentLength == -1 ? data.Length - i : Math.Min(data.Length - i, ContentLength - CurrentReadBytes);
                        sliced = new byte[len];
                        Buffer.BlockCopy(data, i, sliced, 0, len);
                        i += len;
                        CurrentReadBytes += len;
                        ContentTransform.Write(data);
                        if (ContentTransform.Buffered > 0)
                            Readable.Write(ContentTransform.Read());
                        if (CurrentReadBytes == ContentLength)
                        {
                            State = BodyParserState.Dormant;
                            OnEnd?.Invoke();
                            break;
                        }
                        break;
                    case BodyParserState.Chunked_Length:
                        c = (char)data[i++];
                        if (c != CR) StringQueue.Append(c);
                        else
                        {
                            if (!int.TryParse(StringQueue.Next(), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int result))
                                { End(); return -1; }
                            chunkLen = result;
                            State = BodyParserState.Chunked_LenLf;
                        }
                        break;
                    case BodyParserState.Chunked_LenLf:
                        c = (char)data[i++];
                        if (c != LF) { End(); return -1; }
                        if (chunkLen == 0)
                            State = BodyParserState.Chunked_Trailer;
                        else State = BodyParserState.Chunked_ChunkData;
                        break;
                    case BodyParserState.Chunked_ChunkData:
                        len = Math.Min(data.Length - i, chunkLen - chunkIndex);
                        sliced = new byte[len];
                        Buffer.BlockCopy(data, i, sliced, 0, len);
                        i += len;
                        CurrentReadBytes += len;
                        chunkIndex += len;
                        ContentTransform.Write(sliced);
                        Readable.Write(ContentTransform.Read());
                        if (chunkLen == chunkIndex)
                            State = BodyParserState.Chunked_ChunkCr;
                        break;
                    case BodyParserState.Chunked_ChunkCr:
                        c = (char)data[i++];
                        if (c != CR) { End(); return -1; }
                        State = BodyParserState.Chunked_ChunkLf;
                        break;
                    case BodyParserState.Chunked_ChunkLf:
                        c = (char)data[i++];
                        if (c != LF) { End(); return -1; }
                        State = BodyParserState.Chunked_Length;
                        chunkLen = chunkIndex = 0;
                        StringQueue.New();
                        break;
                    case BodyParserState.Chunked_Trailer:
                        c = (char)data[i++];
                        if (c != CR) chunkIndex++;
                        else
                        {
                            if (chunkIndex == 0) State = BodyParserState.Chunked_Lf;
                            else chunkIndex = -1; // LF will be added
                        }
                        break;
                    case BodyParserState.Chunked_Lf:
                        c = (char)data[i++];
                        if (c != LF) { End(); return -1; }
                        State = BodyParserState.Dormant;
                        OnEnd?.Invoke();
                        ContentLength = CurrentReadBytes;
                        break;
                    default: throw new InvalidOperationException("ProcessData cannot execute on Dormant state");
                }
            }
            if (writeExcess)
            {
                len = data.Length - i;
                sliced = new byte[len];
                Buffer.BlockCopy(data, i, sliced, 0, len);
                Writable.Write(sliced);
            }
            return i;
        }

        public void Reset()
        {
            if (TransferEncoding != TransferEncoding.None)
            {
                ContentTransform.End();
                ContentTransform = null;
            }
            StringQueue = null;
            chunkLen = chunkIndex = CurrentReadBytes = 0;
            State = BodyParserState.Dormant;
            TransferEncoding = TransferEncoding.None;
            CompressionType = CompressionType.Unknown;
            ContentLength = -1;
            IsSet = false;
            HasProcessedData = false;
        }

        public byte[] ReadExcess() => Writable.Read();
        public byte[] ReadExcess(int length) => Writable.Read(length);
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);
        public override void Write(byte[] data) => ProcessData(data, true);
        public int WriteSafe(byte[] data) => ProcessData(data, false);

        public override void End()
        {
            base.End();
            if (IsSet) Reset();
        }
    }
}
