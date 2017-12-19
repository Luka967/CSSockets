using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class ResponseHead : MessageHead
    {
        public ushort StatusCode { get; set; }
        public string StatusDescription { get; set; }

        public ResponseHead() : base()
        {
            StatusCode = 0;
            StatusDescription = null;
        }
        public ResponseHead(HttpVersion version) : base()
        {
            StatusCode = 200;
            StatusDescription = "OK";
            Version = version;
        }
    }
}
