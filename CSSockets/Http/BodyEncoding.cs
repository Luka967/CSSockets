using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http
{
    // this distinguishing is definetly on the transport layer
    public enum TransferEncoding : sbyte
    {
        None = -1,
        Raw = 0,
        Chunked = 1
    }
    // this may be done at the transport layer but we can't be sure
    // so we can make this set the compression of the content
    public enum ContentEncoding : sbyte
    {
        Unknown = -1,
        Binary = 0,
        Gzip = 1,
        Deflate = 2,
        Compress = 3
    }
}
