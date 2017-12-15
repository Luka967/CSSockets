using System.Text;
using CSSockets.Streams;
using System.IO.Compression;

namespace CSSockets.Http.BodyParsing
{
    // literally just RawUnifiedDuplex but inherits BodyParser's type distinguishing
    sealed public class Binary : BodyParser
    {
        public override TransferType TransferType => TransferType.Binary;
        public override CompressionType CompressionType => CompressionType.None;

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread();
        public override void Write(byte[] data) => Bhandle(data);
    }

    abstract public class CompressedBinary : BodyParser
    {
        // only transfer type is known - compression type is to be determined with inheritage
        public override TransferType TransferType => TransferType.Binary;

        protected DecompressorDuplex Decompressor { get; set; }

        public CompressedBinary(DecompressorDuplex decompressor)
            => Decompressor = decompressor;

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            Decompressor.Write(data);
            Bhandle(Decompressor.Read());
        }

        public override void End()
        {
            base.End();
            Decompressor.End();
        }
    }

    sealed public class BinaryGzip : CompressedBinary
    {
        public override CompressionType CompressionType => CompressionType.Gzip;
        public BinaryGzip() : base(new GzipDecompressor()) { }
    }

    sealed public class BinaryDeflate : CompressedBinary
    {
        public override CompressionType CompressionType => CompressionType.Deflate;
        public BinaryDeflate() : base(new DeflateDecompressor()) { }
    }
}
