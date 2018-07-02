using System.Collections.Generic;

namespace CSSockets.WebSockets.Definition
{
    public abstract class Negotiator
    {
        protected abstract bool CheckExtensions(NegotiatingExtension[] extensions);
        protected abstract IEnumerable<NegotiatingExtension> RequestExtensions();
        protected abstract IEnumerable<NegotiatingExtension> RespondExtensions(NegotiatingExtension[] requested);
    }
}
