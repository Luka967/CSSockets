using System;
using System.Text;
using CSSockets.Streams;
using System.IO.Compression;
using System.Collections.Generic;

namespace CSSockets.Http
{
    // don't use flags - hardcode it
    public enum HttpBodyType
    {
        Binary = 0,
        Chunked = 1,
        BinaryGzip = 2,
        BinaryDeflate = 3,
        ChunkedGzip = 4,
        ChunkedDeflate = 5
    }
    sealed public class HttpBodyParser : BaseDuplex
    {

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);

        public override void Write(byte[] data)
        {
            throw new NotImplementedException();
        }
    }
}
