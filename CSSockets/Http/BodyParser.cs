using System;
using CSSockets.Streams;
using System.Globalization;

namespace CSSockets.Http
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
    sealed public class BodyParser : UnifiedDuplex
    {
        private const char CR = '\r';
        private const char LF = '\n';

        public UnifiedDuplex ContentTransform { get; private set; } = null;
        private RawUnifiedDuplex ExcessReadable { get; set; } = new RawUnifiedDuplex();
        public TransferEncoding TransferEncoding { get; private set; } = TransferEncoding.None;
        public ContentEncoding ContentEncoding { get; private set; } = ContentEncoding.Unknown;

        public int ContentLength { get; set; } = -1; // -1 = unknown, 0 = none, 1+ = has body
        public int CurrentReadBytes { get; set; } = 0;
        private int chunkLen = 0, chunkIndex = 0;
        private StringQueue StringQueue { get; set; } = null;

        private bool IsSet { get; set; } = false;
        private BodyParserState State { get; set; } = BodyParserState.Dormant;

        public bool TrySetFor(HttpHead request)
        {
            ThrowIfEnded();
            // partial of RFC 7320's 3.3.3 is used
            TransferEncoding transfer = TransferEncoding.Raw;
            ContentEncoding content = ContentEncoding.Binary;
            int contentLen = -1;
            if (request.Headers["Content-Length"] != null)
            {
                if (!int.TryParse(request.Headers["Content-Length"], out int len))
                    return false;
                contentLen = len;
            }
            // does Transfer-Encoding actually have priority?
            string joined = (request.Headers["Transfer-Encoding"] ?? "") + (request.Headers["Content-Encoding"] ?? "");
            if (joined == "") { Reset(transfer, content, contentLen); return true; }
            string[] split = joined.Split(',');
            for (int i = 0; i < split.Length; i++)
            {
                switch (split[i].Trim())
                {
                    case "chunked":
                        if (contentLen != -1) return false; // 3.3.3.3
                        transfer = TransferEncoding.Chunked;
                        break;
                    case "gzip":
                        if (content != ContentEncoding.Binary) return false; // multiple compression algorithms
                        content = ContentEncoding.Gzip;
                        break;
                    case "deflate":
                        if (content != ContentEncoding.Binary) return false; // multiple compression algorithms
                        content = ContentEncoding.Deflate;
                        break;
                    case "compress":
                        return false; // not implemented
                    default: return false; // unknown encoding
                }
            }
            Reset(transfer, content, contentLen);
            return true;
        }

        private void Reset(TransferEncoding transfer, ContentEncoding content = ContentEncoding.Unknown, int contentLength = -1)
        {
            if (IsSet)
            {
                ContentTransform.End();
                ContentTransform = null;
                StringQueue = null;
                ContentLength = -1;
                chunkLen = chunkIndex = CurrentReadBytes = 0;
                State = BodyParserState.Dormant;
            }
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
                ContentEncoding = content;
                switch (content)
                {
                    case ContentEncoding.Binary: ContentTransform = new RawUnifiedDuplex(); break;
                    case ContentEncoding.Gzip: ContentTransform = new GzipDecompressor(); break;
                    case ContentEncoding.Deflate: ContentTransform = new DeflateDecompressor(); break;
                    case ContentEncoding.Compress: throw new Exception("Got Compress, an unimplemented compression, as content encoding");
                    default: throw new Exception("Got Unknown as content encoding");
                }
            }
            IsSet = true;
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
                        Bhandle(ContentTransform.Read());
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
                        Bhandle(ContentTransform.Read());
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
                ExcessReadable.Write(sliced);
            }
            return i;
        }

        public byte[] ReadExcess(bool block)
        {
            ThrowIfEnded();
            if (!block && ExcessReadable.Buffered == 0) return new byte[0]; // or should it be null?
            return ExcessReadable.Read();
        }
        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data) => ProcessData(data, true);
        public int WriteSafe(byte[] data) => ProcessData(data, false);

        public override void End()
        {
            base.End();
            ExcessReadable.End();
            if (IsSet) ContentTransform.End();
        }
    }
}
