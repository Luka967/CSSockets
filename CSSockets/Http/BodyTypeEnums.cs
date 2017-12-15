using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http
{
    public enum TransferType : sbyte
    {
        // implicit
        Binary = 0,

        Chunked = 1,
        Custom = 2
    }
    public enum CompressionType : byte
    {
        // implicit
        None = 0,

        Gzip = 1,
        Deflate = 2,
        Custom = 3
    }
}
