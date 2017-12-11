using System;
using System.Text;
using WebSockets.Tcp;
using WebSockets.Streams;
using System.Collections.Generic;

namespace WebSockets.Http
{
    public class HttpConnection : BaseDuplex
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