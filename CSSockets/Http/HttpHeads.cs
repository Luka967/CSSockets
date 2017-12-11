using System;
using System.Collections.Generic;
using System.Text;

namespace WebSockets.Http
{
    abstract public class HttpHead
    {
        public HttpVersion Version { get; internal set; }
        public HttpHeaders Headers { get; internal set; }
    }

    public class HttpRequestHead : HttpHead
    {
        public string Method { get; internal set; }
        public HttpQuery Query { get; internal set; }

        public HttpRequestHead()
        {
            Query = null;
            Version = null;
            Headers = new HttpHeaders();
        }
    }

    public class HttpResponseHead : HttpHead
    {
        public ushort StatusCode { internal get; set; }
        public string StatusDescription { internal get; set; }

        public HttpResponseHead()
        {
            StatusCode = 0;
            StatusDescription = null;
            Version = null;
            Headers = new HttpHeaders();
        }
        public HttpResponseHead(HttpVersion version)
        {
            StatusCode = 200;
            StatusDescription = "OK";
            Version = version;
            Headers = new HttpHeaders();
        }
    }
}
