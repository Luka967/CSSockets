using CSSockets.Http.Base;
using CSSockets.Http.Reference;

namespace CSSockets.Http.Primitives
{
    public struct BodyType
    {
        public int ContentLength { get; }
        public TransferEncoding TransferEncoding { get; }
        public CompressionType CompressionType { get; }

        public BodyType(int contentLength, TransferEncoding transferEncoding, CompressionType contentEncoding) : this()
        {
            ContentLength = contentLength;
            TransferEncoding = transferEncoding;
            CompressionType = contentEncoding;
        }

        public static BodyType? TryDetectFor(MessageHead head)
        {
            // RFC 7320's 3.3.3 is used
            TransferEncoding transfer = TransferEncoding.Raw;
            CompressionType content = CompressionType.None;
            int contentLen = -1;
            if (head.Headers["Content-Length"] != null)
            {
                if (!int.TryParse(head.Headers["Content-Length"], out int len))
                    return null;
                contentLen = len;
            }
            string joined = (head.Headers["Transfer-Encoding"] ?? "");
            if (head.Headers["Content-Encoding"] != null) joined += ", " + head.Headers["Content-Encoding"];
            if (joined == "")
            {
                if (contentLen == -1 && head is RequestHead)
                    // 3.3.3.6
                    return new BodyType(-1, TransferEncoding.None, CompressionType.Unknown);
                return new BodyType(contentLen, transfer, content);
            }
            string[] split = joined.Split(',');
            for (int i = 0; i < split.Length; i++)
            {
                switch (split[i].Trim())
                {
                    case "chunked":
                        if (contentLen != -1) return null; // 3.3.3.3
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

        public override string ToString() => string.Format("{0} {1} (content length: {2})", TransferEncoding, CompressionType, ContentLength);
    }
}
