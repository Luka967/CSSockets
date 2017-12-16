using System.Text;
using CSSockets.Streams;

namespace CSSockets.Http
{
    abstract public class HeadSerializer<T> : BaseReadable
        where T : HttpHead, new()
    {
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);

        protected const char WHITESPACE = ' ';
        protected const char EQUALS = '=';
        protected const string CRLF = "\r\n";

        abstract public void Write(T head);
    }

    sealed public class RequestHeadSerializer : HeadSerializer<HttpRequestHead>
    {
        public override void Write(HttpRequestHead head)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Method + WHITESPACE + head.Query + WHITESPACE + head.Version + CRLF);
            foreach (Header header in head.Headers.AsCollection())
                builder.Append(header.Name + EQUALS + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }

    sealed public class ResponseHeadSerializer : HeadSerializer<HttpResponseHead>
    {
        public override void Write(HttpResponseHead head)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Version.ToString() + WHITESPACE + head.StatusCode + WHITESPACE + head.StatusDescription + CRLF);
            foreach (Header header in head.Headers.AsCollection())
                builder.Append(header.Name + EQUALS + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }
}
