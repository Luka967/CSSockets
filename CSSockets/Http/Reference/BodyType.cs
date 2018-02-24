using CSSockets.Http.Base;

namespace CSSockets.Http.Reference
{
    public enum TransferEncoding : sbyte
    {
        None = -1,
        Raw = 0,
        Chunked = 1
    }
    public enum CompressionType : sbyte
    {
        Unknown = -1,
        None = 0,
        Gzip = 1,
        Deflate = 2,
        Compress = 3
    }

    public struct BodyType
    {
        public ulong? ContentLength { get; }
        public TransferEncoding TransferEncoding { get; }
        public CompressionType CompressionType { get; }

        public BodyType(ulong? contentLength, TransferEncoding transferEncoding, CompressionType contentEncoding) : this()
        {
            ContentLength = contentLength;
            TransferEncoding = transferEncoding;
            CompressionType = contentEncoding;
        }

        public static BodyType? TryDetectFor(Head head, bool defaultNoBody)
        {
            // RFC 7320's 3.3.3 is used
            TransferEncoding transfer = TransferEncoding.Raw;
            CompressionType content = CompressionType.None;
            ulong? contentLen = null;
            if (head.Headers["Content-Length"] != null)
            {
                if (!ulong.TryParse(head.Headers["Content-Length"], out ulong len))
                    return null;
                contentLen = len;
            }
            if (head.Headers["Transfer-Encoding"] == null)
            {
                if (contentLen == null && defaultNoBody)
                    // 3.3.3.6
                    return new BodyType(null, TransferEncoding.None, CompressionType.Unknown);
                return new BodyType(contentLen, transfer, content);
            }
            string[] split = head.Headers["Transfer-Encoding"].Split(',');
            for (int i = 0; i < split.Length; i++)
            {
                switch (split[i].Trim())
                {
                    case "chunked":
                        if (contentLen != null) return null; // 3.3.3.3
                        transfer = TransferEncoding.Chunked;
                        break;
                    case "gzip":
                        if (content != CompressionType.None)
                            return null; // multiple compression algorithms
                        content = CompressionType.Gzip;
                        break;
                    case "deflate":
                        if (content != CompressionType.None)
                            return null; // multiple compression algorithms
                        content = CompressionType.Deflate;
                        break;
                    case "compress":
                        return null; // not implemented
                    default: return null; // unknown encoding
                }
            }
            return new BodyType(contentLen, transfer, content);
        }

        public override string ToString() => string.Format("{0} transfer {1} compression ({2} content length)", TransferEncoding, CompressionType, ContentLength);
    }
}
