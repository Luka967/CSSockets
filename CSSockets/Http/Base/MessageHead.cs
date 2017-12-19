using CSSockets.Http.Primitives;

namespace CSSockets.Http.Base
{
    abstract public class MessageHead
    {
        public HttpVersion Version { get; set; }
        public HeaderCollection Headers { get; set; }

        public MessageHead()
        {
            Headers = new HeaderCollection();
            Version = null;
        }
    }
}
