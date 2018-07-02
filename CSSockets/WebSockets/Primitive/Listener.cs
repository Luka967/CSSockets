using System.Net;
using System.Linq;
using CSSockets.Http.Reference;
using System.Collections.Generic;
using CSSockets.WebSockets.Definition;

namespace CSSockets.WebSockets.Primitive
{
    public delegate void ConnectionHandler(Connection connection);
    public class Listener : Definition.Listener
    {
        public event ConnectionHandler OnConnection;

        public Listener() : base() { }
        public Listener(EndPoint endPoint) : base(endPoint) { }
        public Listener(Tcp.Listener listener) : base(listener) { }
        public Listener(Http.Reference.Listener listener) : base(listener) { }

        protected override bool CheckExtensions(NegotiatingExtension[] requested) => true;
        protected override IEnumerable<NegotiatingExtension> RespondExtensions(NegotiatingExtension[] requested) => Enumerable.Empty<NegotiatingExtension>();
        protected override void FireConnection(Tcp.Connection connection, RequestHead req, byte[] trail)
        {
            Connection newConnection = new Connection(connection, req, new Definition.Connection.ServerMode());
            OnConnection?.Invoke(newConnection);
            newConnection.WriteTrail(trail);
        }
    }
}
