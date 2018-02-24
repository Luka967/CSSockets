using System.IO;
using System.IO.Compression;

namespace CSSockets.Streams
{
    public abstract class CompressorDuplex : UnifiedDuplex, IFinishable
    {
        protected readonly MemoryStream Cbuffer = new MemoryStream();
        protected Stream Cstream;
        public CompressionLevel Level { get; }
        public bool Finished { get; private set; } = false;

        public CompressorDuplex(CompressionLevel level) => Level = level;

        protected bool Coutput()
        {
            lock (Rlock) lock (Wlock)
                {
                    Bhandle(Cbuffer.Pbuffer.Read(Cbuffer.Pbuffer.Length));
                    return true;
                }
        }

        public override bool Write(byte[] source)
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (Finished) return false;
                Cstream.Write(source, 0, source.Length);
                return Coutput();
            }
        }
        public override bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));

        public override byte[] Read() => Bread();
        public override ulong Read(byte[] destination) => Bread(destination);
        public override byte[] Read(ulong length) => Bread(length);

        public bool Finish()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Finished) return false;
                    Finished = true;
                    Cstream.Dispose();
                    bool result = Coutput();
                    Cbuffer.Dispose();
                    return result;
                }
        }
        public override bool End()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (!Finished) Finish();
                    return base.End();
                }
        }
    }
    public class DeflateCompressor : CompressorDuplex
    {
        public DeflateCompressor(CompressionLevel level) : base(level) => Cstream = new DeflateStream(Cbuffer, level, true);
    }
    public class GzipCompressor : CompressorDuplex
    {
        public GzipCompressor(CompressionLevel level) : base(level) => Cstream = new GZipStream(Cbuffer, level, true);
    }

    public abstract class DecompressorDuplex : UnifiedDuplex, IFinishable
    {
        public const int DEFAULT_BUFFER_SIZE = 1024;

        protected readonly MemoryStream Cbuffer = new MemoryStream();
        protected Stream Cstream;
        public bool Finished { get; private set; } = false;
        public ulong BufferSize { get; set; } = DEFAULT_BUFFER_SIZE;

        protected bool Coutput()
        {
            lock (Rlock) lock (Wlock)
                {
                    byte[] data = new byte[BufferSize]; int read;
                    while (true)
                    {
                        try { read = Cstream.Read(data, 0, data.Length); }
                        catch (InvalidDataException) { return false; }
                        if (read == 0) break;
                        Bhandle(PrimitiveBuffer.Slice(data, 0, read));
                    }
                    return true;
                }
        }

        public override bool Write(byte[] source)
        {
            lock (Wlock)
            {
                if (Ended) return false;
                if (Finished) return false;
                Cbuffer.Write(source, 0, source.Length);
                return Coutput();
            }
        }
        public override bool Write(byte[] source, ulong start, ulong end)
            => Write(PrimitiveBuffer.Slice(source, start, end));

        public override byte[] Read() => Bread();
        public override ulong Read(byte[] destination) => Bread(destination);
        public override byte[] Read(ulong length) => Bread(length);

        public bool Finish()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (Finished) return false;
                    Finished = true;
                    Cstream.Dispose();
                    Cbuffer.Dispose();
                    return true;
                }
        }
        public override bool End()
        {
            lock (Rlock) lock (Wlock)
                {
                    if (!Finished) Finish();
                    return base.End();
                }
        }
    }
    public class DeflateDecompressor : DecompressorDuplex
    {
        public DeflateDecompressor() => Cstream = new DeflateStream(Cbuffer, CompressionMode.Decompress, true);
    }
    public class GzipDecompressor : DecompressorDuplex
    {
        public GzipDecompressor() => Cstream = new GZipStream(Cbuffer, CompressionMode.Decompress, true);
    }
}
