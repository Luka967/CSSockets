using System.Text;
using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class RequestHeadSerializer : HeadSerializer<RequestHead>
    {
        public override void Write(RequestHead head)
        {
            ThrowIfEnded();
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Method + WHITESPACE + head.Query + WHITESPACE + head.Version + CRLF);
            foreach (Header header in head.Headers.AsCollection())
                builder.Append(header.Name + COLON + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }
}
