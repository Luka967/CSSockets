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
        public bool IsCompressing { get; private set; } = false;
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;
        public TransferEncoding TransferEncoding { get; private set; } = TransferEncoding.None;
        public CompressionType ContentEncoding { get; private set; } = CompressionType.Unknown;
        public int ContentLength { get; private set; } = -1;
        private int Processed { get; set; } = -1;

        private bool IsSet { get; set; } = false;

        public bool TrySetFor(MessageHead head)
        {
            ThrowIfEnded();
            BodyType? bodyType = BodyType.TryDetectFor(head);
            if (bodyType == null) return false;

            CompressionType content = bodyType.Value.CompressionType;
            TransferEncoding transfer = bodyType.Value.TransferEncoding;
            int contentLength = bodyType.Value.ContentLength;

            if (IsSet) Finish();

            IsSet = true;
            TransferEncoding = transfer;
            ContentEncoding = content;
            ContentLength = contentLength;
            switch (content)
            {
                case CompressionType.None: IsCompressing = false; ContentTransform = new RawUnifiedDuplex(); break;
                case CompressionType.Gzip: IsCompressing = true; ContentTransform = new GzipCompressor(CompressionLevel); break;
                case CompressionType.Deflate: IsCompressing = true; ContentTransform = new DeflateCompressor(CompressionLevel); break;
                case CompressionType.Compress: throw new NotImplementedException("Got Compress, an unimplemented compression, as content encoding");
                default: throw new ArgumentException("Got Unknown as content encoding");
            }
            return true;
        }
        public void SetFor(MessageHead head)
        {
            if (!TrySetFor(head)) throw new ArgumentException("Could not figure out body encoding");
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
                    }
                    else
                        // last chunk
                        Bhandle(LAST_CHUNK);
                    break;
            }
        }

        public void Finish()
        {
            ThrowIfEnded();
            if (!IsSet) throw new InvalidOperationException("Not set");
            if (IsCompressing)
            {
                // compressed data footer
                CompressorDuplex compressor = ContentTransform as CompressorDuplex;
                compressor.Finish();
                WriteTransformed(compressor.Read());
            }
            WriteTransformed(null);
            IsSet = false;
            ContentTransform.End();
            IsCompressing = false;
            TransferEncoding = TransferEncoding.None;
            ContentEncoding = CompressionType.Unknown;
            ContentLength = -1;
            Processed = 0;
        }

        public override void End()
        {
            if (IsSet) Finish();
            base.End();
        }
    }
}
