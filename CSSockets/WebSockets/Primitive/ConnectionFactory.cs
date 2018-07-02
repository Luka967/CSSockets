using System.Linq;
using CSSockets.Http.Reference;
using System.Collections.Generic;
using CSSockets.WebSockets.Definition;

namespace CSSockets.WebSockets.Primitive
{
    public class ConnectionFactory : ConnectionFactory<Connection>
    {
        public static readonly ConnectionFactory Default = new ConnectionFactory();

        protected override bool CheckExtensions(NegotiatingExtension[] requested) => requested.Length == 0;
        protected override IEnumerable<NegotiatingExtension> RequestExtensions() => Enumerable.Empty<NegotiatingExtension>();
        protected override Connection GenerateConnection(Tcp.Connection connection, RequestHead req) => new Connection(connection, req, new Definition.Connection.ClientMode());
    }
}
