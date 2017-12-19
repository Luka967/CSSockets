using CSSockets.Streams;

namespace CSSockets.Http.Base
{
    abstract public class HeadSerializer<T> : BaseReadable
        where T : MessageHead, new()
    {
        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);

        protected const char WHITESPACE = ' ';
        protected const char COLON = ':';
        protected const string CRLF = "\r\n";

        abstract public void Write(T head);
    }
}
