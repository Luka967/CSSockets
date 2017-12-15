using CSSockets.Streams;
using System.IO.Compression;

namespace CSSockets.Http.BodySerializing
{
    // literally just RawUnifiedDuplex but inherits BodySerializer's type distinguishing
    sealed public class Binary : BodySerializer
    {
        public override TransferType TransferType => TransferType.Binary;
        public override CompressionType CompressionType => CompressionType.None;

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data) => Bhandle(data);
    }

    abstract public class CompressedBinary : BodySerializer
    {
        // only transfer type is known - compression type is to be determined with inheritage
        public override TransferType TransferType => TransferType.Binary;

        protected CompressorDuplex Compressor { get; set; }
        public CompressionLevel CompressionLevel => Compressor.CompressionLevel;

        public CompressedBinary(CompressorDuplex compressor)
            => Compressor = compressor;

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            Compressor.Write(data);
            Bhandle(Compressor.Read());
        }

        public override void End()
        {
            base.End();
            Compressor.End();
        }
    }

    sealed public class BinaryGzip : CompressedBinary
    {
        public override CompressionType CompressionType => CompressionType.Gzip;
        public BinaryGzip(CompressionLevel compressionLevel) :
            base(new GzipCompressor(compressionLevel))
        { }
    }

    sealed public class BinaryDeflate : CompressedBinary
    {
        public override CompressionType CompressionType => CompressionType.Deflate;
        public BinaryDeflate(CompressionLevel compressionLevel) :
            base(new DeflateCompressor(compressionLevel))
        { }
    }
}
