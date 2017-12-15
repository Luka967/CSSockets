using CSSockets.Streams;

namespace CSSockets.Http.BodySerializing
{
    abstract public class BodySerializer : UnifiedDuplex
    {
        abstract public TransferType TransferType { get; }
        abstract public CompressionType CompressionType { get; }
    }
}
