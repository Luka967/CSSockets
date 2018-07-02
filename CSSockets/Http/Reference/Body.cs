using System;
using System.Text;
using CSSockets.Streams;
using System.Globalization;
using CSSockets.Http.Definition;

namespace CSSockets.Http.Reference
{
    public sealed class BodySerializer : Duplex, IFinishable
    {
        private const string CRLF = "\r\n";
        private static readonly byte[] CRLF_BYTES = Encoding.ASCII.GetBytes(CRLF);
        private static readonly byte[] LAST_CHUNK = Encoding.ASCII.GetBytes("0" + CRLF + CRLF);

        public event ControlHandler OnFinish;

        public bool Finished { get; private set; } = true;
        public BodyType? Type { get; private set; } = null;
        public ulong CurrentLength { get; private set; } = 0;
        public ulong CurrentEncodedLength { get; private set; } = 0;

        private Duplex Transform = null;
        private bool IsTransforming = false;
        private bool IsTransformFinishable = false;

        public bool TrySetFor(BodyType type)
        {
            lock (Sync)
            {
                if (!Finished) return false;
                if (type.Compression == TransferCompression.Compress)
                    return false;
                if (type.Encoding == TransferEncoding.None)
                    return true;
                Finished = false;
                Type = type;
                switch (type.Compression)
                {
                    case TransferCompression.None:
                        IsTransforming = false;
                        break;
                    case TransferCompression.Deflate:
                        IsTransforming = true;
                        Transform = new DeflateCompressor();
                        IsTransformFinishable = true;
                        break;
                    case TransferCompression.Gzip:
                        IsTransforming = true;
                        Transform = new GzipCompressor();
                        IsTransformFinishable = true;
                        break;
                }
                return true;
            }
        }

        protected sealed override bool HandleWritable(byte[] source)
        {
            if (IsTransforming)
            {
                if (!Transform.Write(source)) return false;
                if (Transform.BufferedReadable > 0) return WriteChunk(Transform.Read());
                return true;
            }
            return WriteChunk(source);
        }
        private bool WriteChunk(byte[] source)
        {
            switch (Type.Value.Encoding)
            {
                case TransferEncoding.None: return false;
                case TransferEncoding.Binary:
                    CurrentLength = CurrentEncodedLength += (ulong)source.LongLength;
                    if (CurrentEncodedLength > Type.Value.Length) return false;
                    return HandleReadable(source);
                case TransferEncoding.Chunked:
                    string str = source.Length.ToString("X");
                    if (!HandleReadable(Encoding.ASCII.GetBytes(str))) return false;
                    if (!HandleReadable(CRLF_BYTES)) return false;
                    if (!HandleReadable(source)) return false;
                    if (!HandleReadable(CRLF_BYTES)) return false;
                    CurrentLength += (ulong)source.LongLength;
                    CurrentEncodedLength += (ulong)str.Length + 2 + (ulong)source.LongLength + 2;
                    return true;
                default: return false;
            }
        }
        private bool WriteLast()
        {
            switch (Type.Value.Encoding)
            {
                case TransferEncoding.None: return false;
                case TransferEncoding.Binary: return true;
                case TransferEncoding.Chunked: return HandleReadable(LAST_CHUNK);
                default: return false;
            }
        }
        public bool Finish()
        {
            lock (Sync)
            {
                if (Finished) return false;
                if (IsTransforming)
                {
                    if (IsTransformFinishable && !(Transform as IFinishable).Finish())
                        return false;
                    if (Transform.BufferedReadable > 0 && !WriteChunk(Transform.Read()))
                        return false;
                }
                if (!WriteLast()) return false;
                Transform = null;
                IsTransforming = false;
                IsTransformFinishable = false;
                Finished = true;
                Type = null;
                CurrentLength = CurrentEncodedLength = 0;
                OnFinish?.Invoke();
                return true;
            }
        }
    }

    public sealed class BodyParser : Duplex, IFinishable
    {
        private enum ParseState : byte
        {
            Dormant,
            Binary,
            Chunked_Length,
            Chunked_LengthLf,
            Chunked_ChunkData,
            Chunked_ChunkCr,
            Chunked_ChunkLf,
            Chunked_Trailer,
            Chunked_Lf
        }
        private const char CR = '\r';
        private const char LF = '\n';

        private ParseState State = ParseState.Dormant;
        public bool Finished => State == ParseState.Dormant;
        public bool Malformed { get; private set; } = false;
        public event ControlHandler OnFinish;

        private Duplex ExcessStore = new MemoryDuplex();
        public IReadable Excess => ExcessStore;

        private Duplex Transform = null;
        private bool IsTransforming = false;
        private bool IsTransformFinishable = false;

        private string ChunkLengthString = null;
        private ulong ChunkIndex = 0;
        private ulong ChunkLength = 0;

        public BodyType? Type { get; private set; } = null;
        public ulong ContentLength { get; private set; } = 0;
        public ulong EncodedContentLength { get; private set; } = 0;

        public bool TrySetFor(BodyType type)
        {
            lock (Sync)
            {
                if (State != ParseState.Dormant) return false;
                if (type.Compression == TransferCompression.Compress)
                    return false;
                if (type.Encoding == TransferEncoding.None)
                    return true;
                Type = type;
                switch (type.Compression)
                {
                    case TransferCompression.None:
                        IsTransforming = false;
                        break;
                    case TransferCompression.Deflate:
                        IsTransforming = true;
                        Transform = new DeflateDecompressor();
                        IsTransformFinishable = true;
                        break;
                    case TransferCompression.Gzip:
                        IsTransforming = true;
                        Transform = new GzipDecompressor();
                        IsTransformFinishable = true;
                        break;
                }
                switch (type.Encoding)
                {
                    case TransferEncoding.Binary: State = ParseState.Binary; break;
                    case TransferEncoding.Chunked: State = ParseState.Chunked_Length; break;
                }
                return true;
            }
        }

        protected sealed override bool HandleWritable(byte[] source)
        {
            if (Malformed) return false;
            char c = '\0'; ulong length, i = 0, sourceLength = (ulong)source.LongLength;
            for (; i < sourceLength;)
                switch (State)
                {
                    case ParseState.Dormant: return false;
                    case ParseState.Binary:
                        ContentLength = EncodedContentLength += sourceLength;
                        if (!WriteChunk(source)) return false;
                        if (Type.Value.Length.HasValue && Type.Value.Length == ContentLength)
                            return Finish();
                        return true;
                    case ParseState.Chunked_Length:
                        EncodedContentLength++;
                        c = (char)source[i++];
                        if (c == CR) State = ParseState.Chunked_LengthLf;
                        else ChunkLengthString = ChunkLengthString == null ? c.ToString() : ChunkLengthString + c;
                        break;
                    case ParseState.Chunked_LengthLf:
                        EncodedContentLength++;
                        c = (char)source[i++];
                        if (c != LF) return !(Malformed = true);
                        ChunkIndex = 0;
                        if (!ulong.TryParse(ChunkLengthString, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out length))
                            return !(Malformed = true);
                        ChunkLengthString = null;
                        State = (ChunkLength = length) == 0 ? ParseState.Chunked_Trailer : ParseState.Chunked_ChunkData;
                        break;
                    case ParseState.Chunked_ChunkData:
                        length = Math.Min(ChunkLength - ChunkIndex, sourceLength - i);
                        WriteChunk(PrimitiveBuffer.Slice(source, i, i += length));
                        ChunkIndex += length; ContentLength += length; EncodedContentLength += length;
                        if (ChunkIndex >= ChunkLength) State = ParseState.Chunked_ChunkCr;
                        break;
                    case ParseState.Chunked_ChunkCr:
                        EncodedContentLength++;
                        c = (char)source[i++];
                        if (c != CR) return !(Malformed = true);
                        State = ParseState.Chunked_ChunkLf;
                        break;
                    case ParseState.Chunked_ChunkLf:
                        EncodedContentLength++;
                        c = (char)source[i++];
                        if (c != LF) return !(Malformed = true);
                        ChunkIndex = 0;
                        State = ParseState.Chunked_Length;
                        break;
                    case ParseState.Chunked_Trailer:
                        while ((c = (char)source[i++]) != CR) EncodedContentLength++;
                        State = ParseState.Chunked_Lf;
                        break;
                    case ParseState.Chunked_Lf:
                        EncodedContentLength++;
                        c = (char)source[i++];
                        if (c != LF) return !(Malformed = true);
                        ExcessStore.Write(source, i);
                        return Finish();
                }
            return true;
        }
        private bool WriteChunk(byte[] source)
        {
            if (IsTransforming)
            {
                if (!Transform.Write(source)) return false;
                if (Transform.BufferedReadable > 0) return HandleReadable(Transform.Read());
                return true;
            }
            return HandleReadable(source);
        }

        public bool Finish()
        {
            lock (Sync)
            {
                if (State == ParseState.Dormant) return false;
                State = ParseState.Dormant;
                if (IsTransforming)
                {
                    if (IsTransformFinishable && !(Transform as IFinishable).Finish())
                        return false;
                    if (Transform.BufferedReadable > 0 && !HandleReadable(Transform.Read()))
                        return false;
                }
                Transform = null;
                Malformed = false;
                IsTransforming = IsTransformFinishable = false;
                ChunkLengthString = null;
                ChunkIndex = ChunkLength = 0;
                OnFinish?.Invoke();
                return true;
            }
        }
    }
}
