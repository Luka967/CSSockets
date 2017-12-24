namespace CSSockets.Http.Primitives
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
}
