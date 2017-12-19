using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
    sealed public class RequestHead : MessageHead
    {
        private string _Method;
        public string Method { get => _Method; set => _Method = value.ToUpperInvariant(); }
        public Query Query { get; set; }

        public RequestHead() : base()
        {
            _Method = null;
            Query = null;
        }
    }
}
