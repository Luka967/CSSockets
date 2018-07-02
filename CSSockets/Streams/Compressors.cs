using System.IO;
using System.IO.Compression;

namespace CSSockets.Streams
{
    public class Compressor<T> : Duplex, IFinishable where T : Stream
    {
        protected readonly SimpleStream Backstore = new SimpleStream();
        protected T Transformer;
        public CompressionLevel CompressionLevel { get; }
        public bool Finished { get; private set; } = false;

        protected Compressor(CompressionLevel compressionLevel) => CompressionLevel = compressionLevel;

        protected bool PipeTransformer()
        {
            byte[] ret = new byte[Backstore.Length];
            Backstore.Read(ret, 0, (int)Backstore.Length);
            return HandleReadable(ret);
        }
        protected override bool HandleWritable(byte[] source)
        {
            if (Finished) throw new System.ObjectDisposedException("Stream has ended", null as System.Exception);
            Transformer.Write(source, 0, source.Length);
            return PipeTransformer();
        }

        public bool Finish()
        {
            lock (Sync)
            {
                if (Finished) return false;
                Transformer.Dispose();
                PipeTransformer();
                Backstore.Dispose();
                return Finished = true;
            }
        }
    }
    public class DeflateCompressor : Compressor<DeflateStream>
    {
        public DeflateCompressor(CompressionLevel compressionLevel = CompressionLevel.Optimal) : base(compressionLevel)
            => Transformer = new DeflateStream(Backstore, compressionLevel);
    }
    public class GzipCompressor : Compressor<GZipStream>
    {
        public GzipCompressor(CompressionLevel compressionLevel = CompressionLevel.Optimal) : base(compressionLevel)
            => Transformer = new GZipStream(Backstore, compressionLevel);
    }

    public class Decompressor<T> : Duplex, IFinishable where T : Stream
    {
        public const int TRANSFER_SIZE_DEFAULT = 1024;

        protected readonly SimpleStream Backstore = new SimpleStream();
        protected T Transformer;
        public int TransferSize { get; set; }
        public bool Finished { get; private set; } = false;

        protected Decompressor(int transferSize) => TransferSize = transferSize;

        protected bool PipeTransformer()
        {
            byte[] ret = new byte[TransferSize]; int len;
            while ((len = Transformer.Read(ret, 0, ret.Length)) > 0)
                if (!HandleReadable(PrimitiveBuffer.Slice(ret, 0, len))) return false;
            return true;
        }
        protected override bool HandleWritable(byte[] source)
        {
            if (Finished) throw new System.ObjectDisposedException("Stream has ended", null as System.Exception);
            Backstore.Write(source, 0, source.Length);
            return PipeTransformer();
        }

        public bool Finish()
        {
            lock (Sync)
            {
                if (Finished) return false;
                Transformer.Dispose();
                Backstore.Dispose();
                return Finished = true;
            }
        }
    }
    public class DeflateDecompressor : Decompressor<DeflateStream>
    {
        public DeflateDecompressor(int transferSize = TRANSFER_SIZE_DEFAULT) : base(transferSize) => Transformer = new DeflateStream(Backstore, CompressionMode.Decompress);
    }
    public class GzipDecompressor : Decompressor<GZipStream>
    {
        public GzipDecompressor(int transferSize = TRANSFER_SIZE_DEFAULT) : base(transferSize) => Transformer = new GZipStream(Backstore, CompressionMode.Decompress);
    }
}
