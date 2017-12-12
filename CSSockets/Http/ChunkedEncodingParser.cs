using System;
using System.Text;
using CSSockets.Streams;
using System.Collections.Generic;

namespace CSSockets.Http
{
    public class HttpChunkedEncodingParser : UnifiedDuplex
    {
        public override byte[] Read()
        {
            throw new NotImplementedException();
        }

        public override byte[] Read(int length)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] data)
        {
            throw new NotImplementedException();
        }
    }
}
