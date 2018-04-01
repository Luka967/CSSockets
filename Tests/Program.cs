using System;
using System.Net;
using CSSockets.Tcp;
using System.Threading;
using CSSockets.Streams;
using System.Net.Sockets;
using CSSockets.Http.Base;
using System.Net.Security;
using CSSockets.WebSockets;
using System.Collections.Generic;
using CSSockets.Http.Reference;
using CSSockets.Http.Structures;
using static System.Text.Encoding;
using TcpListener = CSSockets.Tcp.Listener;
using System.Security.Cryptography.X509Certificates;

namespace Test
{
    public static class Program
    {
        static void Main(string[] args)
        {
            WebSocketStreamTest(args);
        }

        public static void WebSocketStreamTest(string[] args)
        {
            WebSocketListener listener = new WebSocketListener(new IPEndPoint(IPAddress.Any, 420));
            listener.OnConnection += (ws) =>
            {
                new Thread(() =>
                {
                    Thread.Sleep(1000);
                    WebSocket.Streamer streamer = ws.Stream();
                    //streamer.Cork();
                    StreamWriter writer = new StreamWriter(streamer);
                    writer.WriteFloat32LE(float.NaN);
                    writer.WriteFloat32LE(2);
                    writer.WriteFloat32LE(8);
                    writer.WriteFloat32LE(25);
                    writer.WriteFloat32LE(-9825);
                    writer.WriteFloat32LE(float.NegativeInfinity);
                    streamer.End();
                }).Start();
            };
            listener.Start();
            Console.ReadKey();
            listener.Stop();
        }

        public static void StreamReaderWriterTest(string[] args)
        {
            MemoryDuplex duplex = new MemoryDuplex();
            StreamReader reader = new StreamReader(duplex);
            StreamWriter writer = new StreamWriter(duplex);

            // reading
            // little endian
            duplex.Write(BitConverter.GetBytes(10));
            duplex.Write(BitConverter.GetBytes(10u));
            duplex.Write(BitConverter.GetBytes(10f));
            duplex.Write(BitConverter.GetBytes(10d));
            duplex.Write(UTF8.GetBytes("Test"));
            Console.WriteLine(reader.ReadInt32LE());
            Console.WriteLine(reader.ReadUInt32LE());
            Console.WriteLine(reader.ReadFloat32LE());
            Console.WriteLine(reader.ReadFloat64LE());
            Console.WriteLine(reader.ReadStringUTF8ZT());

            // big endian
            duplex.Write(BitConverter.GetBytes(10));
            duplex.Write(BitConverter.GetBytes(10u));
            duplex.Write(BitConverter.GetBytes(10f));
            duplex.Write(BitConverter.GetBytes(10d));
            duplex.Write(BigEndianUnicode.GetBytes("Test12345"));
            duplex.Write(BitConverter.GetBytes((ushort)0));
            byte[] reversed = duplex.Read();
            Array.Reverse(reversed, 0, 4);
            Array.Reverse(reversed, 4, 4);
            Array.Reverse(reversed, 8, 4);
            Array.Reverse(reversed, 12, 8);
            duplex.Write(reversed);
            Console.WriteLine(reader.ReadInt32BE());
            Console.WriteLine(reader.ReadUInt32BE());
            Console.WriteLine(reader.ReadFloat32BE());
            Console.WriteLine(reader.ReadFloat64BE());
            Console.WriteLine(reader.ReadStringUnicodeBEZT());

            // reading & writing
            // little endian
            writer.WriteUInt8(100);
            writer.WriteInt8(-100);
            writer.WriteUInt16LE(100);
            writer.WriteUInt16BE(100);
            writer.WriteIntBE(-100, 24);
            writer.WriteStringUnicodeBE("One two three");
            writer.WriteUInt16BE(0);
            Console.WriteLine(reader.ReadUInt8());
            Console.WriteLine(reader.ReadInt8());
            Console.WriteLine(reader.ReadUInt16LE());
            Console.WriteLine(reader.ReadUInt16BE());
            Console.WriteLine(reader.ReadIntBE(24));
            Console.WriteLine(reader.ReadStringUnicodeBEZT());
            duplex.End();
            Console.ReadKey();
        }

        public static void TcpSocketEchoTest(string[] args)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Loopback, 420);
            TcpListener listener = new TcpListener(endPoint);
            ulong connections = 0;
            listener.OnConnection += (server) =>
            {
                connections++;
                server.Pipe(server);
                server.OnClose += () => connections--;
                server.TimeoutAfter = new TimeSpan(0, 0, 0, 0, 500);
            };
            Console.WriteLine("open");
            listener.Start();
            while (true)
            {
                Console.WriteLine(connections);
                if (Console.KeyAvailable) break;
                Thread.Sleep(500);
            }
            Console.WriteLine("ended check loop");
            Console.ReadKey();
            Thread.Sleep(2000);
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

            TcpSocketScalabilityMetrics metrics = new TcpSocketScalabilityMetrics();
            metrics.attempts = 2000;

            TcpListener listener = new TcpListener();
            listener.Backlog = metrics.attempts;
            listener.BindEndPoint = serverendPoint;
            listener.OnConnection += (server) =>
            {
                Interlocked.Increment(ref metrics.serverActive);
                server.TimeoutAfter = new TimeSpan(0, 0, 5);
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

            List<Connection> clients = new List<Connection>();

            for (int i = 0; i < metrics.attempts; i++)
            {
                Interlocked.Increment(ref metrics.clientCreated);
                Connection client = new Connection();
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
                    Console.WriteLine(metrics.ToString());
            }

            Console.WriteLine("clients created");

            for (int i = 0; i < metrics.attempts; i++)
                clients[i].Connect(clientEndPoint);

            Console.WriteLine("clients connecting");

            while (true)
            {
                Console.WriteLine(metrics.ToString());
                if (IOControl.ConnectionCount == 0) break;
                Thread.Sleep(200);
            }
            listener.Stop();
            Console.WriteLine("done");
            Console.ReadKey();
        }

        public static void WebSocketListenerTest(string[] args)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 420);
            WebSocketListener listener = new WebSocketListener(endPoint);
            listener.OnConnection += (connection) =>
            {
                Console.WriteLine("connection");
                connection.OnBinary += (data) => Console.WriteLine(data.LongLength);
                connection.OnString += (str) =>
                {
                    connection.Send(new byte[125]);
                    connection.Send(new byte[126]);
                    connection.Send(new byte[127]);
                    connection.Send(new byte[65534]);
                    connection.Send(new byte[65535]);
                    connection.Send(new byte[65536]);
                };
            };
            listener.Start();
            Console.ReadKey();
            listener.Stop();
            Console.ReadKey();
        }

        public static void HttpListenerConnectionTest(string[] args)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 420);
            CSSockets.Http.Reference.Listener listener = new CSSockets.Http.Reference.Listener(endPoint);
            listener.OnConnection += (conn) => Console.WriteLine("New connection from {0}", conn.Base.RemoteAddress);
            listener.OnRequest = (req, res) =>
            {
                Console.WriteLine(req.Method + " " + req.Query + " " + req.Version);
                res["Content-Length"] = "13";
                res.SendHead();
                byte[] data = UTF8.GetBytes("HELLO WORLD!!");
                res.Write(data);
                res.End();
            };
            listener.Start();
            Console.ReadKey();
            listener.Stop();
            Console.ReadKey();
        }

        public static void BodyTransformingTest(string[] args)
        {
            BodyParser parser = new BodyParser();
            BodySerializer serializer = new BodySerializer();
            serializer.Pipe(parser);
            parser.OnData += (data) => Console.WriteLine("Parser got data, length {0}", data.LongLength);
            parser.OnFinish += () => Console.WriteLine("Parser finish");
            serializer.OnFinish += () => Console.WriteLine("Serializer finish");
            parser.OnFail += () => Console.WriteLine("Parser pipe forward fail");
            serializer.OnFail += () => Console.WriteLine("Serializer pipe forward fail");

            Head head = new Head()
            {
                Version = "HTTP/1.1",
                Headers = new HeaderCollection()
                {
                    ["Content-Encoding"] = "deflate"
                }
            };
            BodyType? type = BodyType.TryDetectFor(head, false);
            if (type == null)
            {
                Console.WriteLine("Failed getting body type for head.");
                return;
            }
            Console.WriteLine("Parser set succeded: {0}", parser.TrySetFor(type.Value));
            Console.WriteLine("Serializer set succeded: {0}", serializer.TrySetFor(type.Value));
            Console.WriteLine("Parser set compression: {0}", parser.SetCompression(CompressionType.Deflate));
            Console.WriteLine("Serializer set compression: {0}", serializer.SetCompression(CompressionType.Deflate));

            serializer.Write(new byte[100]);
            serializer.Write(new byte[100]);
            Console.WriteLine("Serializer end: {0}", serializer.End());
            Console.WriteLine("Parser end: {0}", parser.End());
            Console.ReadKey();
        }

        public static void BodyDetectorTest(string[] args)
        {
            Head head = new Head()
            {
                Version = "HTTP/1.1",
                Headers = new HeaderCollection()
                {
                    ["Content-Length"] = "133769",
                    ["Transfer-Encoding"] = "deflate"
                }
            };
            Console.WriteLine(BodyType.TryDetectFor(head, true)?.ToString() ?? "Failed");
            Console.ReadKey();
        }

        public static void ResponseHeadParserMalformedTest(string[] args)
        {
            ResponseHeadParser parser = new ResponseHeadParser();
            parser.OnFail += () => Console.WriteLine("Write fail");
            parser.Write(UTF8.GetBytes("HTTP/1.1 2000 OK\r\nKey:             Value\r\n\r\n"));
            if (!parser.Ended && parser.Queued > 0) Console.WriteLine("Parsed");
            else Console.WriteLine("Not parsed (ended: {0})", parser.Ended);
            Console.ReadKey();
            parser.End();
        }

        public static void ResponseHeadTransformingTest(string[] args)
        {
            ResponseHeadParser parser = new ResponseHeadParser();
            ResponseHeadSerializer serializer = new ResponseHeadSerializer();
            serializer.Pipe(parser);
            serializer.Write(new ResponseHead()
            {
                Version = "HTTP/1.1",
                StatusCode = 200,
                StatusDescription = "OK",
                Headers = new HeaderCollection()
                {
                    new Header("Host", "localhost"),
                    new Header("Origin", "localhost"),
                    new Header("Key", "qwerty")
                }
            });
            ResponseHead head = parser.Next();
            Console.WriteLine(head.Version + " " + head.StatusCode + " " + head.StatusDescription);
            foreach (Header header in head.Headers) Console.WriteLine(header.Name + ": " + header.Value);
            Console.ReadKey();
            parser.End();
            serializer.End();
        }

        public static void RequestHeadParserMalformedTest(string[] args)
        {
            RequestHeadParser parser = new RequestHeadParser();
            parser.OnFail += () => Console.WriteLine("Write fail");
            parser.Write(UTF8.GetBytes("GET / HTTP/256.0\r\nKey : Value\r\n\r\n"));
            if (!parser.Ended && parser.Queued > 0) Console.WriteLine("Parsed");
            else Console.WriteLine("Not parsed (ended: {0})", parser.Ended);
            Console.ReadKey();
            parser.End();
        }

        public static void RequestHeadTransformingTest(string[] args)
        {
            RequestHeadParser parser = new RequestHeadParser();
            RequestHeadSerializer serializer = new RequestHeadSerializer();
            serializer.Pipe(parser);
            serializer.Write(new RequestHead()
            {
                Method = "GET",
                Query = new Query("/etc/passwd?loggedIn=true#user"),
                Version = "HTTP/1.1",
                Headers = new HeaderCollection()
                {
                    new Header("Host", "localhost"),
                    new Header("Origin", "localhost"),
                    new Header("Key", "qwerty")
                }
            });
            RequestHead head = parser.Next();
            Console.WriteLine(head.Method + " " + head.Query + " " + head.Version);
            foreach (Header header in head.Headers) Console.WriteLine(header.Name + ": " + header.Value);
            Console.ReadKey();
            parser.End();
            serializer.End();
        }

        private static void CopyBenchmarkSample(byte[] data, string name, Action<byte[]> action, int count)
        {
            DateTime start = DateTime.UtcNow;
            for (int i = 0; i < count; i++) action(data);
            Console.WriteLine("{0}: {1} samples in {2:F2}ms", name, count, (DateTime.UtcNow - start).TotalMilliseconds);
        }
        public static unsafe void CopyBenchmark(string[] args)
        {
            Random rand = new Random();
            byte[] sample = new byte[400];
            rand.NextBytes(sample);
            int sampleCount = 1000000;
            Console.WriteLine("Sample size: {0}", sample.LongLength);
            Console.WriteLine("Sample count: {0}" + Environment.NewLine, sampleCount);
            CopyBenchmarkSample(sample, "Array.Copy", (data) =>
            {
                byte[] identical = new byte[data.LongLength];
                Array.Copy(data, 0, identical, 0, data.LongLength);
            }, sampleCount);
            CopyBenchmarkSample(sample, "Buffer.BlockCopy", (data) =>
            {
                byte[] identical = new byte[data.LongLength];
                Buffer.BlockCopy(data, 0, identical, 0, data.Length);
            }, sampleCount);
            CopyBenchmarkSample(sample, "Memcpy", (data) =>
            {
                byte[] identical = new byte[data.LongLength];
                PrimitiveBuffer.Copy(data, 0, identical, 0, data.Length);
            }, sampleCount);
            CopyBenchmarkSample(sample, "Buffer.MemoryCopy", (data) =>
            {
                byte[] identical = new byte[data.LongLength];
                fixed (void* samplep = sample, identicalp = identical)
                    Buffer.MemoryCopy(samplep, identicalp, data.LongLength, data.LongLength);
            }, sampleCount);
            Console.ReadKey();
        }

        public static void TcpSocketTest(string[] args)
        {
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Loopback, 42069);
            TcpListener listener = new TcpListener(endPoint);
            Connection server;
            listener.OnConnection += (_server) =>
            {
                server = _server;
                server.AllowHalfOpen = true;
                server.TimeoutAfter = new TimeSpan(0, 0, 5);

                Console.WriteLine("SERVER OPEN");
                server.OnClose += () => Console.WriteLine("SERVER CLOSE");
                server.OnData += (data) => Console.WriteLine("SERVER DATA LEN {0}", data.LongLength);
                server.OnDrain += () => Console.WriteLine("SERVER DRAIN");
                server.OnEnd += () => Console.WriteLine("SERVER END");
                server.OnError += (e) => Console.WriteLine("SERVER ERROR {0}", e.ToString());
                server.OnTimeout += () => { Console.WriteLine("SERVER TIMEOUT"); server.Terminate(); };

                server.Write(new byte[1000]);
                server.End();
            };
            listener.Start();
            Connection client = new Connection();
            client.OnOpen += () =>
            {
                Console.WriteLine("CLIENT OPEN");
                client.End();
            };
            client.OnClose += () => Console.WriteLine("CLIENT CLOSE");
            client.OnData += (data) => Console.WriteLine("CLIENT DATA LEN {0}", data.LongLength);
            client.OnDrain += () => Console.WriteLine("CLIENT DRAIN");
            client.OnEnd += () => { Console.WriteLine("CLIENT END"); client.End(); };
            client.OnError += (e) => Console.WriteLine("CLIENT ERROR {0}", e.ToString());
            client.OnTimeout += () => Console.WriteLine("CLIENT TIMEOUT");
            client.AllowHalfOpen = true;
            client.Connect(endPoint);

            Thread.Sleep(500);
            listener.Stop();
            Console.ReadKey();
        }

        public static void CompressorTest(string[] args)
        {
            DeflateCompressor compressor = new DeflateCompressor(System.IO.Compression.CompressionLevel.NoCompression);
            DeflateDecompressor decompressor = new DeflateDecompressor();
            compressor.OnData += (data) =>
            {
                Console.WriteLine("COMPRESSOR DATA {0}", data.ByteArrayToString());
                decompressor.Write(data);
            };
            compressor.OnDrain += () => Console.WriteLine("COMPRESSOR DRAIN");
            compressor.OnEnd += () => Console.WriteLine("COMPRESSOR END");
            decompressor.OnData += (data) => Console.WriteLine("DECOMPRESSOR DATA {0}", data.ByteArrayToString());
            decompressor.OnDrain += () => Console.WriteLine("DECOMPRESSOR DRAIN");
            decompressor.OnEnd += () => Console.WriteLine("DECOMPRESSOR END");
            compressor.Write(new byte[] { 1, 2, 3, 4, 5 });
            compressor.Finish();
            compressor.End();
            decompressor.Finish();
            decompressor.End();
            Console.ReadKey();
        }

        public static void MemoryBufferTest(string[] args)
        {
            Random random = new Random();
            MemoryDuplex buffer = new MemoryDuplex();
            buffer.OnData += (data) => Console.WriteLine("DATA {0}", data.Length > 50 ? data.Length.ToString() : data.ByteArrayToString());
            buffer.OnDrain += () => Console.WriteLine("DRAIN");
            buffer.OnEnd += () => Console.WriteLine("END");
            byte[] b = new byte[10000000];
            random.NextBytes(b);
            buffer.Write(b);
            buffer.End();
            Console.ReadKey();
        }

        public static void PrimitiveBufferTest(string[] args)
        {
            PrimitiveBuffer buffer = new PrimitiveBuffer();

            Console.WriteLine(buffer.Buffer.ByteArrayToString());
            buffer.Write(new byte[] { 1, 2, 3 });
            Console.WriteLine(buffer.Buffer.ByteArrayToString());
            buffer.Write(new byte[] { 255, 255, 255, 255, 255 });
            Console.WriteLine(buffer.Buffer.ByteArrayToString());
            buffer.Read(new byte[5]);
            Console.WriteLine(buffer.Buffer.ByteArrayToString());

            Console.ReadKey();
        }
    }

    public static class Extensions
    {
        public static string ByteArrayToString(this byte[] array)
        {
            if (array.LongLength == 0) return "";
            string s = "";
            foreach (byte item in array) s += item.ToString("X2");
            return s;
        }
    }
}
