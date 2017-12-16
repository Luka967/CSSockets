using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http
{
    internal static class BodyDetector
    {
        public static BodyType? TryDetectFor(HttpHead head)
        {
            // RFC 7320's 3.3.3 is used
            TransferEncoding transfer = TransferEncoding.Raw;
            ContentEncoding content = ContentEncoding.Binary;
            int contentLen = -1;
            if (head.Headers["Content-Length"] != null)
            {
                if (!int.TryParse(head.Headers["Content-Length"], out int len))
                    return null;
                contentLen = len;
            }
            // does Transfer-Encoding actually have priority?
            string joined = (head.Headers["Transfer-Encoding"] ?? "") + (head.Headers["Content-Encoding"] ?? "");
            if (joined == "")
            {
                if (contentLen != -1 && head is HttpRequestHead)
                    // 3.3.3.6
                    return new BodyType(-1, TransferEncoding.None, ContentEncoding.Unknown);
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
                        if (content != ContentEncoding.Binary) return null; // multiple compression algorithms
                        content = ContentEncoding.Gzip;
                        break;
                    case "deflate":
                        if (content != ContentEncoding.Binary) return null; // multiple compression algorithms
                        content = ContentEncoding.Deflate;
                        break;
                    case "compress":
                        return null; // not implemented
                    default: return null; // unknown encoding
                }
            }
            return new BodyType(contentLen, transfer, content);
        }
    }

    internal struct BodyType
    {
        public int ContentLength { get; }
        public TransferEncoding TransferEncoding { get; }
        public ContentEncoding ContentEncoding { get; }

        public BodyType(int contentLength, TransferEncoding transferEncoding, ContentEncoding contentEncoding) : this()
        {
            ContentLength = contentLength;
            TransferEncoding = transferEncoding;
            ContentEncoding = contentEncoding;
        }
    }
}
