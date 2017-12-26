using System;
using System.Net;
using CSSockets.Tcp;
using System.Threading;
using System.Net.Sockets;

namespace CSSockets
{
    partial class Program
    {
        static void TcpSocketClientsideTest(string[] args)
        {
            IPEndPoint ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420);
            Socket listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(ep);
            listener.Listen(1);
            ThreadPool.QueueUserWorkItem((_) =>
            {
                TcpSocket client = new TcpSocket();
                client.Connect(ep);
            });
            Socket server = listener.Accept();
            Console.WriteLine("SERVER CONNECTION");
            server.Send(new byte[] { 1, 2, 3, 4, 5 });
            Console.ReadKey();
        }
    }
}
