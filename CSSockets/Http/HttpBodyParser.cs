using System;
using System.Text;
using WebSockets.Streams;
using System.IO.Compression;
using System.Collections.Generic;

namespace WebSockets.Http
{
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
