using System;
using System.Collections.Generic;
using System.Text;

namespace CSSockets.Http
{
    abstract public class HttpHead
    {
        public Version Version { get; internal set; }
        public HeaderCollection Headers { get; internal set; }

        public HttpHead()
        {
            Headers = new HeaderCollection();
            Version = null;
        }
    }

    sealed public class HttpRequestHead : HttpHead
    {
        private string _Method;
        public string Method { get => _Method; internal set => _Method = value.ToUpperInvariant(); }
        public Query Query { get; internal set; }

        public HttpRequestHead() : base()
        {
            _Method = null;
            Query = null;
        }
    }

    sealed public class HttpResponseHead : HttpHead
    {
        public ushort StatusCode { internal get; set; }
        public string StatusDescription { internal get; set; }

        public HttpResponseHead() : base()
        {
            StatusCode = 0;
            StatusDescription = null;
        }
        public HttpResponseHead(Version version) : base()
        {
            StatusCode = 200;
            StatusDescription = "OK";
            Version = version;
        }
    }
}
