using System;
using System.Text;
using CSSockets.Tcp;

namespace CSSockets.WebSockets
{
    public class ServerWebSocket : WebSocket
    {
        public ServerWebSocket(TcpSocket socket, byte[] trail) : base(socket, trail) { }

        public override void Close(ushort code, string reason)
        {
            byte[] reasonBuf = Encoding.UTF8.GetBytes(reason);
            byte[] payload = new byte[2 + reason.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(code), 0, payload, 0, 2);
            Buffer.BlockCopy(reasonBuf, 0, payload, 2, reasonBuf.Length);
            Send(new Frame(true, 8, false, payload, false, false, false));
        }

        public override void Ping(byte[] data = null)
            => Send(new Frame(true, 9, false, data ?? new byte[0], false, false, false));
        public override void Send(byte[] data)
            => Send(new Frame(true, 2, false, data, false, false, false));
        public override void Send(string data)
            => Send(new Frame(true, 1, false, Encoding.UTF8.GetBytes(data), false, false, false));

        protected override void AnswerClose(ushort code, string reason)
        {
            Send(new Frame(true, 8, false, BitConverter.GetBytes(code), false, false, false));
            Base.End();
        }
        protected override void AnswerPing(byte[] data)
            => Send(new Frame(true, 10, false, data, false, false, false));
    }
}
