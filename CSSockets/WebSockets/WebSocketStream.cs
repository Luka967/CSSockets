using CSSockets.Streams;

namespace CSSockets.WebSockets
{
    public abstract partial class WebSocket
    {
        public class Streamer<T> : BaseWritable<T>
            where T : UnifiedDuplex, new()
        {
            public WebSocket Base { get; } = null;

            public Streamer(WebSocket transfer) => Base = transfer;

            protected override void HandleData(byte[] data)
            {
                lock (Base.OpsLock)
                {
                    if (Base.StreamerSentFirst) Base.Send(Base.Behavior.Get(false, 0, data));
                    else Base.Send(Base.Behavior.Get(false, 2, data));
                    Base.StreamerSentFirst = true;
                }
            }

            public override bool End()
            {
                lock (Base.OpsLock)
                {
                    if (Ended) return false;
                    byte[] data = null;
                    if (IsCorked)
                    {
                        Writable.OnData -= HandleData;
                        Writable.Resume();
                        data = Writable.Read();
                        base.End();
                    }
                    else base.End();
                    if (Base.StreamerSentFirst) Base.Send(Base.Behavior.Get(true, 0, data));
                    else Base.Send(Base.Behavior.Get(true, 2, data));
                    Base.IsStreaming = Base.StreamerSentFirst = false;
                    return true;
                }
            }
        }
        public sealed class Streamer : Streamer<MemoryDuplex>
        {
            public Streamer(WebSocket transfer) : base(transfer) { }
        }
    }
}
