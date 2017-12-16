using System;
using System.Text;
using CSSockets.Streams;
using System.Collections.Generic;

namespace CSSockets.Http
{
    sealed public class BodySerializer : UnifiedDuplex
    {
        private const char CR = '\r';
        private const char LF = '\n';

        public UnifiedDuplex ContentTransform { get; private set; } = null;
        public TransferEncoding TransferEncoding { get; private set; } = TransferEncoding.None;
        public ContentEncoding ContentEncoding { get; private set; } = ContentEncoding.Unknown;

        private bool IsSet { get; set; } = false;

        public void SetFor(HttpHead head)
        {

        }
        public void SetFor(TransferEncoding transfer, ContentEncoding content)
        {
            if (IsSet) Finish();

            TransferEncoding = transfer;
            ContentEncoding = content;
            switch (content)
            {
                case ContentEncoding.Binary: ContentTransform = new RawUnifiedDuplex(); break;
                case ContentEncoding.Gzip: ContentTransform = new GzipDecompressor(); break;
                case ContentEncoding.Deflate: ContentTransform = new DeflateDecompressor(); break;
                case ContentEncoding.Compress: throw new Exception("Got Compress, an unimplemented compression, as content encoding");
                default: throw new Exception("Got Unknown as content encoding");
            }
        }

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data)
        {
            
        }

        public void Finish()
        {

        }

        public override void End()
        {
            base.End();
        }
    }
}
