using System;
using System.Net;
using System.Linq;
using CSSockets.Tcp;
using System.Threading;
using CSSockets.Streams;
using System.Diagnostics;
using CSSockets.Http.Reference;
using CSSockets.Http.Definition;
using System.Collections.Generic;
using static System.Text.Encoding;
using TcpListener = CSSockets.Tcp.Listener;
using HttpListener = CSSockets.Http.Reference.Listener;
using WebSocket = CSSockets.WebSockets.Primitive.Connection;
using WebSocketListener = CSSockets.WebSockets.Primitive.Listener;
using WebSocketFactory = CSSockets.WebSockets.Primitive.ConnectionFactory;

namespace CSSockets.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            WebSocketClientTest(args);
        }

        public static void WebSocketClientTest(string[] args)
        {
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, 420);
            IPEndPoint clientEP = new IPEndPoint(IPAddress.Loopback, 420);
            WebSocketListener listener = new WebSocketListener(serverEP);
            WebSocket server = null;
            WebSocket client = null;
            listener.ClientVerifier =
                (address, subprotocols, head) =>
                    subprotocols.Contains("test");
            listener.SubprotocolChooser = (address, subprotocols, head) => "test";
            listener.OnConnection += (connection) =>
            {
                server = connection;
                Console.WriteLine("listener connection");
                Console.WriteLine(server.Subprotocol);
                server.OnBinary += (data) => Console.WriteLine("SERVER BINARY {0}", data.LongLength);
                server.OnString += (data) => Console.WriteLine("SERVER STRING {0}", data.Length);
                server.OnClose += (code, reason) => Console.WriteLine("SERVER CLOSED {0} '{1}'", code, reason);
                server.SendBinary(new byte[1]);
                server.SendBinary(new byte[10]);
                server.SendBinary(new byte[100]);
                server.SendString("Hijklmn");
                server.SendClose(1000, "OK");
            };
            listener.Start();
            Console.WriteLine("listener open");
            Connection clientTcp = new Connection();
            clientTcp.OnOpen += () =>
            {
                client = WebSocketFactory.Default.Generate(clientTcp, "/");
                client.OnOpen += () =>
                {
                    client.OnBinary += (data) => Console.WriteLine("CLIENT BINARY {0}", data.LongLength);
                    client.OnString += (data) => Console.WriteLine("CLIENT STRING {0}", data.Length);
                    client.OnClose += (code, reason) => Console.WriteLine("CLIENT CLOSED {0} '{1}'", code, reason);
                    /*client.SendBinary(new byte[1]);
                    client.SendBinary(new byte[10]);
                    client.SendBinary(new byte[100]);
                    client.SendString("Abcdefg");*/
                    client.SendClose(1000, "OK");
                };
            };
            clientTcp.Connect(clientEP);
            Console.ReadKey();
            Console.WriteLine("client states: {0} {1} {2} {3}", client.Opening, client.Open, client.Closing, client.Closed);
            Console.ReadKey();
            listener.Stop();
            Console.ReadKey();
        }

        public static void WebSocketServerTest(string[] args)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 420);
            WebSocketListener server = new WebSocketListener(endPoint);
            WebSocket client = null;
            server.OnConnection += (connection) =>
            {
                client = connection;
                Console.WriteLine("connection");
                connection.OnBinary += (data) => Console.WriteLine("BINARY {0}", data.LongLength);
                connection.OnString += (data) => Console.WriteLine("STRING {0}", data.Length);
                connection.OnClose += (code, reason) => Console.WriteLine("CLOSED {0} '{1}'", code, reason);
                connection.SendBinary(new byte[1]);
                connection.SendBinary(new byte[10]);
                connection.SendBinary(new byte[100]);
            };
            Console.WriteLine("open");
            server.Start();
            Console.ReadKey();
            client.SendClose(1000, "OK");
            server.Stop();
            Console.ReadKey();
        }

        public static void HttpClientConnectionTest(string[] args)
        {
            IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Loopback, 420);
            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Any, 420);
            HttpListener listener = new HttpListener(serverEndPoint);
            ServerConnection server = null;
            listener.OnRequest = (sreq, res) =>
            {
                Console.WriteLine("Server request");
                server = sreq.Connection;
                if (sreq.Path != "/echo")
                {
                    res["Content-Length"] = "0";
                    res.End(404, "Not Found"); return;
                }
                switch (sreq.Method)
                {
                    case "GET":
                        Console.WriteLine("Server GET request");
                        res["Content-Type"] = "text/plain";
                        res["Transfer-Encoding"] = "chunked";
                        res.SendHead(200, "OK");
                        res.Write(UTF8.GetBytes("GET on /echo\r\nMake a POST request to echo its body"));
                        res.End();
                        Console.WriteLine("Server response finish");
                        break;
                    case "POST":
                        Console.WriteLine("Server POST request");
                        sreq.OnFinish += () =>
                        {
                            Console.WriteLine("Server POST request finish");
                            if (sreq.BufferedReadable == 0) { res.End(400, "Bad Request"); return; }
                            res["Content-Type"] = "text/plain";
                            res["Transfer-Encoding"] = "chunked";
                            res.SendHead(200, "OK");
                            res.Write(UTF8.GetBytes("/ path on POST!\r\n"));
                            res.Write(UTF8.GetBytes("Body is as follows:\r\n\r\n"));
                            byte[] data = sreq.Read();
                            Console.WriteLine(UTF8.GetString(data).Replace("\r", "\\r").Replace("\n", "\\n"));
                            res.Write(data);
                            res.End();
                            Console.WriteLine("Server response finish");
                        };
                        break;
                    default: res.End(400, "Bad Request"); break;
                }
            };
            listener.Start();
            ClientConnection client = null;
            client = new ClientConnection(clientEndPoint, () =>
            {
                OutgoingRequest creq = client.Enqueue("HTTP/1.1", "POST", "/echo");
                creq.OnResponse += (res) =>
                {
                    Console.WriteLine("Client response {0} {1}", res.StatusCode, res.StatusDescription);
                    res.OnFinish += () =>
                    {
                        Console.WriteLine("Client response finish\r\n{0}", UTF8.GetString(res.Read()));
                        client.End();
                        client.Base.End();
                    };
                };
                creq["Transfer-Encoding"] = "chunked";
                creq.SendHead();
                creq.Write(UTF8.GetBytes("I am a body"));
                creq.End();
                Console.WriteLine("Client request finish");
            });
            Console.ReadKey();
            listener.Stop();
            Console.ReadKey();
            //client.Terminate();
        }

        public static void HttpServerConnectionTest(string[] args)
        {
            HttpListener server = new HttpListener(new IPEndPoint(IPAddress.Any, 420));
            server.OnConnection += (connection) => Console.WriteLine("connection");
            server.OnRequest = (req, res) =>
            {
                Console.WriteLine("request");
                if (req.Path == "/favicon.ico") res.End(404, "Not Found");
                else
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    res.OnFinish += () => Console.WriteLine("{0}ms for onfinish fire", sw.Elapsed.TotalMilliseconds.ToString("F2"));
                    res["Transfer-Encoding"] = "chunked";
                    res["Content-Type"] = "text/plain";
                    res["Connection"] = "close";
                    res.SendHead(200, "OK");
                    for (int i = 0; i < 1000000; i++) res.Write(ASCII.GetBytes(i.ToString() + " "));
                    res.End();
                    Console.WriteLine("{0}ms for req end", sw.Elapsed.TotalMilliseconds.ToString("F2"));
                    sw.Stop();
                }
            };
            server.Start();
            Console.WriteLine("started");
            Console.ReadKey();
            server.Stop();
            Console.WriteLine("stopped");
            Console.ReadKey();
        }

        public static void BodyParseSerializeTest(string[] args)
        {
            BodyParser parser = new BodyParser();
            BodySerializer serial = new BodySerializer();

            serial.Pipe(parser);
            serial.OnFail += () => Console.WriteLine("serializer failed");
            parser.OnData += (data) => Console.WriteLine(UTF8.GetString(data));
            parser.OnFinish += () => Console.WriteLine("parser finished");
            parser.Excess.Pipe(VoidWritable.Default);

            BodyType bodyType = new BodyType(null, TransferEncoding.Chunked, TransferCompression.Deflate);
            if (!parser.TrySetFor(bodyType)) Console.WriteLine("parser failed to set");
            if (!serial.TrySetFor(bodyType)) Console.WriteLine("serializer failed to set");

            serial.Write(UTF8.GetBytes("I am a body\r\nxd\r\n"));
            serial.Write(UTF8.GetBytes("I am a body\r\nasfjaskfd\r\nasdfa"));
            serial.Finish();

            Console.ReadKey();
        }

        public static void HeadParseSerializeTest(string[] args)
        {
            RequestHeadParser rParser = new RequestHeadParser();
            ResponseHeadParser RParser = new ResponseHeadParser();
            RequestHeadSerializer rSerial = new RequestHeadSerializer();
            ResponseHeadSerializer RSerial = new ResponseHeadSerializer();

            RequestHead rHead = new RequestHead("HTTP/1.1", "GET", "/");
            rHead.Headers["test1"] = "123";
            rHead.Headers["test2"] = "456";

            ResponseHead RHead = new ResponseHead("HTTP/1.1", 200, "OK");
            RHead.Headers["test1"] = "123";
            RHead.Headers["test2"] = "456";

            rSerial.Pipe(rParser);
            RSerial.Pipe(RParser);

            rParser.OnCollect += (head) => Console.WriteLine("request parser:\n{0}", head.Stringify());
            RParser.OnCollect += (head) => Console.WriteLine("response parser:\n{0}", head.Stringify());
            rSerial.OnFail += () => Console.WriteLine("request serializer failed");
            RSerial.OnFail += () => Console.WriteLine("response serializer failed");

            rSerial.Write(rHead);
            RSerial.Write(RHead);

            Console.ReadKey();
        }

        public static void CompressorTest(string[] args)
        {
            MemoryDuplex memory = new MemoryDuplex();
            GzipCompressor c = new GzipCompressor();
            c.Pipe(memory);
            GzipDecompressor d = new GzipDecompressor();
            memory.Pipe(d);
            d.OnData += (data) => Console.WriteLine("data {0}", data.Stringify());
            c.Write(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
            c.Finish();
            d.Finish();
            Console.ReadKey();
        }

        struct TcpSocketScalabilityMetrics
        {
            public int attempts,
                clientCreated,
                clientActive,
                clientSuccessful,
                clientError,
                serverActive,
                serverTimeout,
                serverSuccessful,
                serverError,
                datasSent,
                datasReceieved;

            public override string ToString()
            {
                return string.Format(
                    "client: {0:0000}c/{1:0000}a/{2:0000}s/{3:0000}e/{4:0000}T" +
                    " server: {5:0000}a/{6:0000}t/{7:0000}s/{8:0000}e/{9:0000}T" +
                    " data: {10:0000}s/{11:0000}r" +
                    " io: {12:0000}c/{13:0000}l/{14:0000}s/{15:00}t",
                    clientCreated, clientActive, clientSuccessful, clientError, clientSuccessful + clientError,
                    serverActive, serverTimeout, serverSuccessful, serverError, serverSuccessful + serverError + serverTimeout,
                    datasSent, datasReceieved,
                    IOControl.ConnectionCount, IOControl.ListenerCount, IOControl.SocketCount, IOControl.ThreadCount
                );
            }
        }

        public static void TcpSocketScalabilityTest(string[] args)
        {
            EndPoint clientEndPoint = new IPEndPoint(IPAddress.Loopback, 420);
            EndPoint serverendPoint = new IPEndPoint(IPAddress.Any, 420);
            byte[] sending = new byte[] { 1, 2, 3, 4, 5 };

            TcpSocketScalabilityMetrics metrics = new TcpSocketScalabilityMetrics
            {
                attempts = 10000
            };

            TcpListener listener = new TcpListener
            {
                Backlog = metrics.attempts,
                BindEndPoint = serverendPoint
            };
            listener.OnConnection += (server) =>
            {
                Interlocked.Increment(ref metrics.serverActive);
                server.TimeoutAfter = new TimeSpan(0, 1, 0);
                server.OnData += (data) => Interlocked.Increment(ref metrics.datasReceieved);
                server.OnError += (e) =>
                {
                    Interlocked.Increment(ref metrics.serverSuccessful);
                    Interlocked.Increment(ref metrics.serverError);
                };
                server.OnClose += () =>
                {
                    Interlocked.Increment(ref metrics.serverSuccessful);
                    Interlocked.Decrement(ref metrics.serverActive);
                };
                server.OnTimeout += () =>
                {
                    Interlocked.Increment(ref metrics.serverTimeout);
                    server.Terminate();
                };
            };
            listener.Start();

            Thread.Sleep(1000);

            DateTime start = DateTime.UtcNow;
            List<Tcp.Connection> clients = new List<Tcp.Connection>();

            Console.WriteLine("CLIENT: created / active / success / error / total SERVER: active / timeout / success / error / total DATA: sent / recvd IO: connections / listeners / sockets / threads");
            for (int i = 0; i < metrics.attempts; i++)
            {
                Interlocked.Increment(ref metrics.clientCreated);
                Tcp.Connection client = new Tcp.Connection();
                client.OnDrain += () => Interlocked.Increment(ref metrics.datasSent);
                client.OnClose += () =>
                {
                    Interlocked.Increment(ref metrics.clientSuccessful);
                    Interlocked.Decrement(ref metrics.clientActive);
                };
                client.OnError += (e) =>
                {
                    if (client.State != TcpSocketState.Open)
                        Interlocked.Increment(ref metrics.clientActive);
                    Interlocked.Decrement(ref metrics.clientSuccessful);
                    Interlocked.Increment(ref metrics.clientError);
                };
                client.OnOpen += () =>
                {
                    Interlocked.Increment(ref metrics.clientActive);
                    client.Write(sending);
                    client.End();
                };
                clients.Add(client);
                if (metrics.clientCreated % 100 == 0)
                    Console.WriteLine("[{0}] {1}", (DateTime.UtcNow - start).TotalSeconds.ToString("F6").PadLeft(12), metrics.ToString());
            }

            Console.WriteLine("[{0}] {1} clients created", (DateTime.UtcNow - start).TotalSeconds.ToString("F6").PadLeft(12), metrics.attempts);

            for (int i = 0; i < metrics.attempts; i++)
                clients[i].Connect(clientEndPoint);

            Console.WriteLine("[{0}] {1} clients connecting", (DateTime.UtcNow - start).TotalSeconds.ToString("F6").PadLeft(12), metrics.attempts);

            Thread.Sleep(100);
            while (true)
            {
                Console.WriteLine("[{0}] {1}", (DateTime.UtcNow - start).TotalSeconds.ToString("F6").PadLeft(12), metrics.ToString());
                if (IOControl.ConnectionCount == 0) break;
                Thread.Sleep(200);
            }
            listener.Stop();
            Console.WriteLine("[{0}] done", (DateTime.UtcNow - start).TotalSeconds.ToString("F6").PadLeft(12));
            Console.ReadKey();
        }
    }

    public static class Extensions
    {
        private const string CR = "\\r\r";
        private const string LF = "\\n\n";
        private const string CRLF = "\\r\\n\r\n";
        private const char COLON = ':';
        private const string WS = " ";

        public static string Stringify(this byte[] array)
        {
            if (array.LongLength == 0) return "";
            string s = "";
            foreach (byte item in array) s += item.ToString("X2");
            return s;
        }

        public static string Stringify(this RequestHead source)
        {
            string stringified = source.Method + WS + source.URL + WS + source.Version + CRLF;
            for (int i = 0; i < source.Headers.Length; i++)
                stringified += source.Headers[i].Key.ToLower() + COLON + WS + source.Headers[i].Value + CRLF;
            stringified += CRLF;
            return stringified;
        }

        public static string Stringify(this ResponseHead source)
        {
            string stringified = source.Version + WS + source.StatusCode + WS + source.StatusDescription + CRLF;
            for (int i = 0; i < source.Headers.Length; i++)
                stringified += source.Headers[i].Key.ToLower() + COLON + WS + source.Headers[i].Value + CRLF;
            stringified += CRLF;
            return stringified;
        }
    }
}
