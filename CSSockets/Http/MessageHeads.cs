using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http
{
    abstract public class HttpHead
    {
        public Version Version { get; internal set; }
        public Headers Headers { get; internal set; }
    }

    sealed public class RequestHead : HttpHead
    {
        public string Method { get; internal set; }
        public Query Query { get; internal set; }

        public RequestHead()
        {
            Query = null;
            Version = null;
            Headers = new Headers();
        }
    }

    sealed public class ResponseHead : HttpHead
    {
        public ushort StatusCode { internal get; set; }
        public string StatusDescription { internal get; set; }

        public ResponseHead()
        {
            StatusCode = 0;
            StatusDescription = null;
            Version = null;
            Headers = new Headers();
        }
        public ResponseHead(Version version)
        {
            StatusCode = 200;
            StatusDescription = "OK";
            Version = version;
            Headers = new Headers();
        }
    }
}
