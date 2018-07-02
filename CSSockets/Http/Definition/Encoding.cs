namespace CSSockets.Http.Definition
{
    public enum TransferEncoding : byte
    {
        None,
        Binary,
        Chunked
    }
    public enum TransferCompression : byte
    {
        None,
        Deflate,
        Gzip,
        Compress
    }
    public struct BodyType
    {
        public ulong? Length { get; }
        public TransferEncoding Encoding { get; }
        public TransferCompression Compression { get; }

        public BodyType(ulong? length, TransferEncoding encoding, TransferCompression compression) : this()
        {
            Length = length;
            Encoding = encoding;
            Compression = compression;
        }

        public static BodyType? TryDetectFor(Head head, bool defaultNoBody)
        {
            // Reference: RFC 7320's 3.3.3
            TransferEncoding encoding = TransferEncoding.Binary;
            TransferCompression compression = TransferCompression.None;
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
                    return new BodyType(null, encoding = TransferEncoding.None, compression);
                return new BodyType(contentLen, encoding, compression);
            }
            string[] split = head.Headers["Transfer-Encoding"].Split(',');
            for (int i = 0; i < split.Length; i++)
            {
                switch (split[i].Trim())
                {
                    case "chunked":
                        if (contentLen != null) return null; // 3.3.3.3
                        encoding = TransferEncoding.Chunked;
                        break;
                    case "gzip":
                        if (compression != TransferCompression.None) return null;
                        compression = TransferCompression.Gzip;
                        break;
                    case "deflate":
                        if (compression != TransferCompression.None) return null;
                        compression = TransferCompression.Deflate;
                        break;
                    case "compress":
                        return null; // not implemented
                    default: return null; // unknown encoding
                }
            }
            return new BodyType(contentLen, encoding, compression);
        }

        public override string ToString() => string.Format("{0} transfer {1} compression ({2} content length)", Encoding, Compression, Length ?? 0);
    }
}
