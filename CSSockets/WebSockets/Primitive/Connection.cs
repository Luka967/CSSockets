using System.Text;
using CSSockets.Streams;
using CSSockets.Http.Reference;
using CSSockets.WebSockets.Definition;

namespace CSSockets.WebSockets.Primitive
{
    public class Connection : Definition.Connection
    {
        public RequestHead Req { get; }

        public Connection(Tcp.Connection connection, RequestHead req, IMode mode) : base(connection, mode) => Req = req;

        public override bool SendBinary(byte[] payload)
        {
            lock (Sync) return Send(new Frame(true, 2, false, false, false, Mode.OutgoingMasked, payload));
        }
        public override bool SendClose(ushort code, string reason = "")
        {
            lock (Sync)
            {
                MemoryDuplex buffer = new MemoryDuplex();
                buffer.Write(new byte[2] { (byte)(code / 256), (byte)(code % 256) });
                buffer.Write(Encoding.UTF8.GetBytes(reason ?? ""));
                return Send(new Frame(true, 8, false, false, false, Mode.OutgoingMasked, buffer.Read())) && StartClose(code, reason);
            }
        }
        protected override bool SendClose(ushort code)
        {
            lock (Sync) return Send(new Frame(true, 8, false, false, false, Mode.OutgoingMasked, new byte[2] { (byte)(code / 256), (byte)(code % 256) }));
        }
        public override bool SendPing(byte[] payload)
        {
            lock (Sync) return Send(new Frame(true, 9, false, false, false, Mode.OutgoingMasked, payload ?? new byte[0]));
        }
        public override bool SendString(string payload)
        {
            lock (Sync) return Send(new Frame(true, 1, false, false, false, Mode.OutgoingMasked, Encoding.UTF8.GetBytes(payload)));
        }
        protected override void OnMergerCollect(Message message)
        {
            lock (Sync)
            {
                if (!HandleMessage(message)) Terminate(1002, "");
            }
        }
        protected override void OnParserCollect(Frame frame)
        {
            lock (Sync)
            {
                if (frame.Masked != Mode.IncomingMasked) { Terminate(1002, ""); return; }
                if (frame.RSV1 || frame.RSV2 || frame.RSV3) { Terminate(1002, ""); return; }
                switch (Merger.Push(frame))
                {
                    case FrameMerger.Response.OK:
                    case FrameMerger.Response.Reset:
                        break;
                    default: Terminate(1002, ""); break;
                }
            }
        }
        protected override bool SendPong(byte[] payload)
        {
            lock (Sync) return Send(new Frame(true, 10, false, false, false, Mode.OutgoingMasked, payload));
        }
    }
}
