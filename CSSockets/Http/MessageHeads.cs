namespace CSSockets.Http
{
    abstract public class HttpHead
    {
        public Version Version { get; set; }
        public HeaderCollection Headers { get; set; }

        public HttpHead()
        {
            Headers = new HeaderCollection();
            Version = null;
        }
    }

    sealed public class HttpRequestHead : HttpHead
    {
        private string _Method;
        public string Method { get => _Method; set => _Method = value.ToUpperInvariant(); }
        public Query Query { get; set; }

        public HttpRequestHead() : base()
        {
            _Method = null;
            Query = null;
        }
    }

    sealed public class HttpResponseHead : HttpHead
    {
        public ushort StatusCode { get; set; }
        public string StatusDescription { get; set; }

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
