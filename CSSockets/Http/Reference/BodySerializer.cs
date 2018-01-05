using System;
using System.Text;
using CSSockets.Streams;
using CSSockets.Http.Base;
using System.IO.Compression;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class BodySerializer : UnifiedDuplex
    {
        private const string CRLF = "\r\n";
        private static readonly byte[] CRLF_BYTES = Encoding.ASCII.GetBytes(CRLF);
        private static readonly byte[] LAST_CHUNK = Encoding.ASCII.GetBytes("0" + CRLF + CRLF);

        private UnifiedDuplex ContentTransform { get; set; } = null;
        public bool IsCompressionSet { get; private set; } = false;
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
        public TransferEncoding TransferEncoding { get; private set; } = TransferEncoding.None;
        public CompressionType Compression { get; private set; } = CompressionType.Unknown;
        public int ContentLength { get; private set; } = -1;

        private bool IsSet { get; set; } = false;

        public bool TrySetFor(MessageHead head)
        {
            ThrowIfEnded();
            BodyType? bodyType = BodyType.TryDetectFor(head);
            if (bodyType == null) return false;

            CompressionType compression = bodyType.Value.CompressionType;
            TransferEncoding transfer = bodyType.Value.TransferEncoding;
            int contentLength = bodyType.Value.ContentLength;

            if (IsSet) Finish();

            IsSet = true;
            TransferEncoding = transfer;
            ContentLength = contentLength;
            if (Compression != CompressionType.Unknown) return true;
            Compression = compression;
            switch (compression)
            {
                case CompressionType.None: ContentTransform = new RawUnifiedDuplex(); IsCompressionSet = false; break;
                case CompressionType.Gzip: ContentTransform = new GzipCompressor(CompressionLevel); IsCompressionSet = true; break;
                case CompressionType.Deflate: ContentTransform = new DeflateCompressor(CompressionLevel); IsCompressionSet = true; break;
                case CompressionType.Compress: return false;
                default: return false;
            }
            return true;
        }
        public void SetFor(MessageHead head)
        {
            if (!TrySetFor(head)) throw new ArgumentException("Could not figure out body encoding");
        }
        public void Compress(CompressionType compressionType)
        {
            if (IsSet) throw new InvalidOperationException("Explicitly setting compression type must be performed before headers get sent");
            Compression = compressionType;
            switch (compressionType)
            {
                case CompressionType.None: ContentTransform = new RawUnifiedDuplex(); IsCompressionSet = false; break;
                case CompressionType.Gzip: ContentTransform = new GzipCompressor(CompressionLevel); IsCompressionSet = true; break;
                case CompressionType.Deflate: ContentTransform = new DeflateCompressor(CompressionLevel); IsCompressionSet = true; break;
                case CompressionType.Compress: throw new NotImplementedException("Got Compress, an unimplemented compression, as compression type");
                default: throw new ArgumentException("Got Unknown as compression type");
            }
        }

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            ThrowIfEnded();
            if (!IsSet) throw new InvalidOperationException("Not set");

            // pass data through ContentTransform
            ContentTransform.Write(data);
            if (ContentTransform.Buffered == 0) return; // no data available
            data = ContentTransform.Read();
            WriteTransformed(data);
        }

        private void WriteTransformed(byte[] data)
        {
            switch (TransferEncoding)
            {
                case TransferEncoding.None: throw new InvalidOperationException("TransferEncoding is None");
                case TransferEncoding.Raw:
                    if (data == null) break;
                    ContentLength += data.Length;
                    Bhandle(data);
                    break;
                case TransferEncoding.Chunked:
                    if (data != null)
                    {
                        string str = data.Length.ToString("X");
                        Bhandle(Encoding.ASCII.GetBytes(str));
                        Bhandle(CRLF_BYTES);
                        Bhandle(data);
                        Bhandle(CRLF_BYTES);
                        ContentLength += str.Length + 2 + data.Length + 2;
                    }
                    else
                    {
                        // last chunk
                        Bhandle(LAST_CHUNK);
                        ContentLength += 5;
                    }
                    break;
            }
        }

        public void Finish()
        {
            ThrowIfEnded();
            if (!IsSet) throw new InvalidOperationException("Not set");
            if (IsCompressionSet)
            {
                // compressed data footer
                CompressorDuplex compressor = ContentTransform as CompressorDuplex;
                compressor.Finish();
                WriteTransformed(compressor.Read());
            }
            WriteTransformed(null);
            IsSet = false;
            ContentTransform.End();
            IsCompressionSet = false;
            TransferEncoding = TransferEncoding.None;
            Compression = CompressionType.Unknown;
            ContentLength = -1;
        }

        public override void End()
        {
            if (IsSet) Finish();
            base.End();
        }
    }
}
