using System;
using System.Collections.Generic;
using System.Text;

namespace WebSockets.Http
{
    public class HttpRequestHead
    {
        public string Method { get; internal set; }
        public HttpQuery Query { get; internal set; }
        public HttpVersion Version { get; internal set; }
        public HttpHeaders Headers { get; internal set; }

        public HttpRequestHead()
        {
            Query = null;
            Version = null;
            Headers = new HttpHeaders();
        }
    }

    public class HttpResponseHead
    {
        public HttpVersion Version { internal get; set; }
        public ushort Code { internal get; set; }
        public string Description { internal get; set; }
        public HttpHeaders Headers { internal get; set; }

        public HttpResponseHead(HttpVersion version)
        {
            Code = 200;
            Description = "OK";
            Version = version;
            Headers = new HttpHeaders();
        }
    }
}
