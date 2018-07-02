namespace CSSockets.WebSockets.Definition
{
    public abstract partial class Connection
    {
        public interface IMode
        {
            bool FireOpen { get; }
            bool IncomingMasked { get; }
            bool OutgoingMasked { get; }
        }
        public struct ClientMode : IMode
        {
            public bool FireOpen => true;
            public bool IncomingMasked => false;
            public bool OutgoingMasked => true;
        }
        public struct ServerMode : IMode
        {
            public bool FireOpen => false;
            public bool IncomingMasked => true;
            public bool OutgoingMasked => false;
        }
    }
}
