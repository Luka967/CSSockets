using System.Text;
using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class ResponseHeadSerializer : HeadSerializer<ResponseHead>
    {
        public override void Write(ResponseHead head)
        {
            ThrowIfEnded();
            StringBuilder builder = new StringBuilder();
            builder.Append(head.Version.ToString() + WHITESPACE + head.StatusCode + WHITESPACE + head.StatusDescription + CRLF);
            foreach (Header header in head.Headers.AsCollection())
                builder.Append(header.Name + COLON + WHITESPACE + header.Value + CRLF);
            builder.Append(CRLF);
            Readable.Write(Encoding.ASCII.GetBytes(builder.ToString()));
        }
    }
}
