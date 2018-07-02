namespace CSSockets.WebSockets.Definition
{
    public abstract partial class Connection
    {
        public interface IMode
        {
            bool IncomingMasked { get; }
            bool OutgoingMasked { get; }
        }
        public struct ClientMode : IMode
        {
            public bool IncomingMasked => false;
            public bool OutgoingMasked => true;
        }
        public struct ServerMode : IMode
        {
            public bool IncomingMasked => true;
            public bool OutgoingMasked => false;
        }
    }
}
