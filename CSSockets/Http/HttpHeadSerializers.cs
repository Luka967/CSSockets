using System.Text;
using WebSockets.Streams;

namespace WebSockets.Http
{
    abstract public class HttpHeadSerializer : BaseReadable { }

    public class HttpRequestHeadSerializer : HttpHeadSerializer
    {
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);

        private const char WHITESPACE = ' ';
        private const char EQUALS = '=';
        private const string CRLF = "\r\n";

        public void Write(HttpRequestHead head)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Method + WHITESPACE + head.Query + WHITESPACE + head.Version + CRLF);
            foreach (HttpHeader header in head.Headers.AsCollection())
                builder.Append(header.Name + EQUALS + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }

    public class HttpResponseHeadSerializer : HttpHeadSerializer
    {
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);

        private const char WHITESPACE = ' ';
        private const char EQUALS = '=';
        private const string CRLF = "\r\n";

        public void Write(HttpResponseHead head)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Version.ToString() + WHITESPACE + head.StatusCode + WHITESPACE + head.StatusDescription + CRLF);
            foreach (HttpHeader header in head.Headers.AsCollection())
                builder.Append(header.Name + EQUALS + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }
}
