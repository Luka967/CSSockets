using System;
using CSSockets.Streams;
using System.Collections.Generic;

namespace CSSockets.Http.BodyParsing
{
    abstract public class BodyParser : UnifiedDuplex
    {
        abstract public TransferType TransferType { get; }
        abstract public CompressionType CompressionType { get; }

        public static BodyParser Pick(HttpHead head)
        {
            List<string> possible = new List<string>();
        }

        public static BodyParser Get(TransferType transferType, CompressionType compressionType)
        {
            switch (transferType)
            {
                case TransferType.Binary:
                    switch (compressionType)
                    {
                        case CompressionType.None: return new Binary();
                        case CompressionType.Gzip: return new BinaryGzip();
                        case CompressionType.Deflate: return new BinaryDeflate();
                        case CompressionType.Custom:
                            throw new ArgumentException("Custom compression types must be explicitly retrieved");
                        default: return null;
                    }
                case TransferType.Chunked:
#if false
                    switch (compressionType)
                    {
                        case CompressionType.None: return new ChunkedBodyParser();
                        case CompressionType.Gzip: return new ChunkedGzipBodyParser();
                        case CompressionType.Deflate: return new ChunkedDeflateBodyParser();
                        case CompressionType.Custom:
                            throw new ArgumentException("Custom compression types must be explicitly retrieved");
                        default: return null;
                    }
#else
                    break;
#endif
                default:
                    throw new ArgumentException("Custom transfer types must be explicitly retrieved");
            }
        }
    }
}
