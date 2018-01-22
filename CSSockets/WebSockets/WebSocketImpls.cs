using System;
using System.Text;
using CSSockets.Tcp;
using CSSockets.Http.Reference;

namespace CSSockets.WebSockets
{
    public class ServerWebSocket : WebSocket
    {
        public ServerWebSocket(TcpSocket socket, RequestHead head) : base(socket, head) { }

        public override void Close(ushort code, string reason)
        {
            byte[] reasonBuf = Encoding.UTF8.GetBytes(reason);
            byte[] payload = new byte[2 + reason.Length];
            payload[0] = (byte)(code >> 8);
            payload[1] = (byte)(code & 255);
            Buffer.BlockCopy(reasonBuf, 0, payload, 2, reasonBuf.Length);
            Console.WriteLine("closed");
            Send(new Frame(true, 8, false, payload, false, false, false));
            InitiateClose(code, reason);
        }

        protected override bool IsValidFrame(Frame frame)
            => frame.Masked && !frame.RSV1 && !frame.RSV2 && !frame.RSV3;

        public override void Ping(byte[] data = null)
            => Send(new Frame(true, 9, false, data ?? new byte[0], false, false, false));
        public override void Send(byte[] data)
            => Send(new Frame(true, 2, false, data, false, false, false));
        public override void Send(string data)
            => Send(new Frame(true, 1, false, Encoding.UTF8.GetBytes(data), false, false, false));

        protected override void AnswerClose(ushort code, string reason)
        {
            byte[] payload = new byte[2 + reason.Length];
            payload[0] = (byte)(code >> 8);
            payload[1] = (byte)(code & 255);
            Send(new Frame(true, 8, false, payload, false, false, false));
            InitiateClose(code, reason);
        }
        protected override void AnswerPing(byte[] data)
            => Send(new Frame(true, 10, false, data, false, false, false));
    }

    public class ClientWebSocket : WebSocket
    {
        public ClientWebSocket(TcpSocket socket, RequestHead head) : base(socket, head) { }

        public override void Close(ushort code, string reason)
        {
            byte[] reasonBuf = Encoding.UTF8.GetBytes(reason);
            byte[] payload = new byte[2 + reason.Length];
            payload[0] = (byte)(code >> 8);
            payload[1] = (byte)(code & 255);
            Buffer.BlockCopy(reasonBuf, 0, payload, 2, reasonBuf.Length);
            Send(new Frame(true, 8, true, payload, false, false, false));
            InitiateClose(code, reason);
        }

        protected override bool IsValidFrame(Frame frame)
            => !frame.Masked && !frame.RSV1 && !frame.RSV2 && !frame.RSV3;

        public override void Ping(byte[] data = null)
            => Send(new Frame(true, 9, true, data ?? new byte[0], false, false, false));
        public override void Send(byte[] data)
            => Send(new Frame(true, 2, true, data, false, false, false));
        public override void Send(string data)
            => Send(new Frame(true, 1, true, Encoding.UTF8.GetBytes(data), false, false, false));

        protected override void AnswerClose(ushort code, string reason)
        {
            byte[] payload = new byte[2 + reason.Length];
            payload[0] = (byte)(code >> 8);
            payload[1] = (byte)(code & 255);
            Send(new Frame(true, 8, true, payload, false, false, false));
            InitiateClose(code, reason);
        }
        protected override void AnswerPing(byte[] data)
            => Send(new Frame(true, 10, true, data, false, false, false));
    }
}
