using CSSockets.Streams;

namespace CSSockets.Http.Definition
{
    public abstract class Head
    {
        public Version Version { get; set; }
        public HeaderCollection Headers { get; }

        public Head() => Headers = new HeaderCollection();
        public Head(Version version) : this() => Version = version;
        public Head(Version version, HeaderCollection headers)
        {
            Version = version;
            Headers = headers;
        }
    }

    public abstract class HeadParser<T> : Translator<T> where T : Head
    {
        protected const char CR = '\r';
        protected const char LF = '\n';
        protected const string CRLF = "\r\n";
        protected const char COLON = ':';
        protected const char WS = ' ';
    }
    public abstract class HeadSerializer<T> : Transform<T> where T : Head
    {
        protected const char CR = '\r';
        protected const char LF = '\n';
        protected const string CRLF = "\r\n";
        protected const char COLON = ':';
        protected const char WS = ' ';
    }
}
