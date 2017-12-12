using System;
using System.IO;
using System.IO.Compression;

namespace CSSockets.Streams
{
    abstract public class CompressorDuplex : UnifiedDuplex
    {
        protected MemoryStream Cstream { get; }
        protected Stream Caccessor { get; set; }
        private object Clock { get; set; }
        public CompressionLevel CompressionLevel { get; set; }
        public bool Finished { get; private set; }
        protected void ThrowIfFinished()
        {
            if (Finished) throw new InvalidOperationException("Cannot perform this operation as the stream cannot write compressed data anymore");
        }

        public CompressorDuplex(CompressionLevel compressionLevel)
        {
            CompressionLevel = compressionLevel;
            Cstream = new MemoryStream();
            Clock = new object();
            Finished = false;
        }

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            ThrowIfEnded();
            ThrowIfFinished();
            lock (Clock)
            {
                Caccessor.Write(data, 0, data.Length);
                Cread();
            }
        }
        private void Cread()
        {
            byte[] compressedChunk = new byte[Cstream.Length];
            Cstream.Position = 0;
            Cstream.Read(compressedChunk, 0, compressedChunk.Length);
            Bhandle(compressedChunk);
            Cstream.Position = 0;
            Cstream.SetLength(0);
        }
        public void Finish()
        {
            ThrowIfEnded();
            ThrowIfFinished();
            lock (Clock)
            {
                Finished = true;
                Caccessor.Dispose();
                Cread();
                Cstream.Dispose();
            }
        }
        public override void End()
        {
            if (!Finished) Finish();
            base.End();
        }
    }

    public class GzipCompressor : CompressorDuplex
    {
        public GzipCompressor(CompressionLevel compressionLevel) : base(compressionLevel)
            => Caccessor = new GZipStream(Cstream, compressionLevel, true);
    }

    public class DeflateCompressor : CompressorDuplex
    {
        public DeflateCompressor(CompressionLevel compressionLevel) : base(compressionLevel)
            => Caccessor = new DeflateStream(Cstream, compressionLevel, true);
    }

    abstract public class DecompressorDuplex : UnifiedDuplex
    {
        protected MemoryStream Cstream { get; }
        protected Stream Caccessor { get; set; }
        private byte[] Cbuffer { get; }
        private object Clock { get; set; }
        public CompressionLevel CompressionLevel { get; set; }
        public bool Finished { get; private set; }
        protected void ThrowIfFinished()
        {
            if (Finished) throw new InvalidOperationException("Cannot perform this operation as the stream cannot write compressed data anymore");
        }

        /// <param name="DCbufferSize">The decompression buffer size</param>
        public DecompressorDuplex(int DCbufferSize = 1024)
        {
            Cstream = new MemoryStream();
            Cbuffer = new byte[DCbufferSize];
            Clock = new object();
            Finished = false;
        }

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            ThrowIfEnded();
            lock (Clock)
            {
                ThrowIfFinished();
                Cstream.Write(data, 0, data.Length);
                Cstream.Position = 0;
                int len;
                while ((len = Caccessor.Read(Cbuffer, 0, Cbuffer.Length)) > 0)
                {
                    byte[] spliced = new byte[len];
                    Buffer.BlockCopy(Cbuffer, 0, spliced, 0, len);
                    Bwrite(spliced);
                }
            }
        }
        public void Finish()
        {
            ThrowIfEnded();
            lock (Clock)
            {
                ThrowIfFinished();
                Finished = true;
                Caccessor.Dispose();
                Cstream.Dispose();
            }
        }
        public override void End()
        {
            if (!Finished) Finish();
            base.End();
        }
    }

    public class GzipDecompressor : DecompressorDuplex
    {
        public GzipDecompressor() : base()
            => Caccessor = new GZipStream(Cstream, CompressionMode.Decompress, true);
    }

    public class DeflateDecompressor : DecompressorDuplex
    {
        public DeflateDecompressor() : base()
            => Caccessor = new DeflateStream(Cstream, CompressionMode.Decompress, true);
    }
}