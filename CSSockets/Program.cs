using System;
using System.IO;
using System.Net;
using System.Text;
using CSSockets.Tcp;
using CSSockets.Base;
using System.Threading;
using CSSockets.Streams;
using CSSockets.Http.Base;
using CSSockets.WebSockets;
using System.IO.Compression;
using CSSockets.Http.Reference;
using CSSockets.Http.Primitives;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CSSockets
{
    class Program
    {
        static void Main(string[] args)
        {
            FrameParserTest(args);
        }
        
        static void FrameParserTest(string[] args)
        {
            FrameParser parser = new FrameParser();
            Frame test = new Frame(true, 2, true, new byte[800000], true, false, true);
            parser.Write(test.Serialize());
            Frame result = parser.Next();
            Console.ReadKey();
        }

        static void ExternalListenerTest(string[] args)
        {
            Lapwatch w = new Lapwatch();
            w.Start();

            Listener listener = new Listener(new IPEndPoint(IPAddress.Parse("0.0.0.0"), 80), "/");
            listener.OnConnection += (conn) =>
            {
                Console.WriteLine("{0:F4} external connection opened", w.Elapsed.TotalMilliseconds);
                conn.Base.OnClose += () => Console.WriteLine("{0:F4} external tcp closed", w.Elapsed.TotalMilliseconds);
                conn.OnEnd += () => Console.WriteLine("{0:F4} external http closed", w.Elapsed.TotalMilliseconds);
            };
            listener.OnRequest = (req, res) =>
            {
                Console.WriteLine("{0:F4} request", w.Elapsed.TotalMilliseconds);
                res.SetHead(200, "OK");
                res["Transfer-Encoding"] = "chunked";
                res["Content-Encoding"] = "gzip";
                res["Server"] = "CSSockets";
                res.Write("This is ");
                res.Write("a chunked body ");
                res.Write("transferred with the ");
                res.Write("\r\nGzip compression ");
                res.Write("algorithm.\r\n\r\nMakan is cool.");
                res.End();
                Console.WriteLine("{0:F4} request finished", w.Elapsed.TotalMilliseconds);
            };
            Console.WriteLine("{0:F4} opening", w.Elapsed.TotalMilliseconds);
            listener.Start();
            Console.WriteLine("{0:F4} opened", w.Elapsed.TotalMilliseconds);
            Console.ReadKey();
            Console.WriteLine("{0:F4} closing", w.Elapsed.TotalMilliseconds);
            listener.Stop();
            Console.WriteLine("{0:F4} closed", w.Elapsed.TotalMilliseconds);
            Console.ReadKey();
            w.Stop();
        }
        
        static void ServerConnectionTest(string[] args)
        {
            TcpListener listener = new TcpListener();
            TcpSocket client = new TcpSocket();
            TcpSocket server = null;
            listener.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            listener.OnConnection += (_server) =>
            {
                server = _server;
                Console.WriteLine("SERVER OPEN");
                ServerConnection conn = new ServerConnection(_server);
                conn.OnMessage = (_req, _res) =>
                {
                    ClientRequest req = _req as ClientRequest;
                    ServerResponse res = _res as ServerResponse;
                    res.SetHead(200, "OK");
                    res.End();
                };
                server.OnClose += () => Console.WriteLine("SERVER CLOSED");
            };
            listener.Start();

            client.OnOpen += () =>
            {
                Console.WriteLine("CLIENT OPEN");
                client.Write(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 4\r\n\r\nTest"));
            };
            client.OnData += (data) => Console.WriteLine("CLIENT {0}", data.ToBase16String());
            client.OnError += (e) => Console.WriteLine("CLIENT ERROR {0}", e);
            client.OnClose += () => Console.WriteLine("CLIENT CLOSED");
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            Console.ReadKey();
        }

        static void RequestBodyParseTest(string[] args)
        {
            // binary uncompressed
            RequestHead request = new RequestHead()
            {
                Method = "POST",
                Headers = new HeaderCollection()
                {
                    ["Transfer-Encoding"] = "chunked, deflate",
                },
                Query = new Query("/"),
                Version = new Http.Primitives.HttpVersion(1, 1)
            };

            BodySerializer serializer = new BodySerializer();
            BodyParser parser = new BodyParser();

            serializer.SetFor(request);
            parser.SetFor(request);

            serializer.Pipe(parser);
            serializer.Write(Encoding.ASCII.GetBytes("Test420"));
            serializer.End();

            Console.WriteLine("parser data: {0}", Encoding.ASCII.GetString(parser.Read()));
            parser.End();
            Console.ReadKey();
        }

        static void BodyParserChunkedTest(string[] args)
        {
            RequestHead request = new RequestHead()
            {
                Method = "GET",
                Headers = new HeaderCollection()
                {
                    new Header("Transfer-Encoding", "chunked"),
                },
                Query = new Query("/"),
                Version = new Http.Primitives.HttpVersion(1, 1)
            };
            BodyParser bodyParser = new BodyParser();
            bodyParser.SetFor(request);
            RawUnifiedDuplex sdf = new RawUnifiedDuplex();
            sdf.Pipe(bodyParser);
            sdf.Write(Encoding.ASCII.GetBytes("4\r\nTest\r\n0\r\nRandom-Header: 420\r\n\r\n"));
            Console.WriteLine(Encoding.ASCII.GetString(bodyParser.Read()));
            Console.ReadKey();
        }

        static void BodyParserBinaryCompressedTest(string[] args)
        {
            RequestHead request = new RequestHead()
            {
                Method = "GET",
                Headers = new HeaderCollection()
                {
                    ["Content-Length"] = "9",
                    ["Content-Encoding"] = "deflate"
                },
                Query = new Query("/"),
                Version = new Http.Primitives.HttpVersion(1, 1)
            };
            BodyParser bodyParser = new BodyParser();
            bodyParser.SetFor(request);
            DeflateCompressor sdf = new DeflateCompressor(CompressionLevel.Optimal);
            sdf.Pipe(bodyParser);
            sdf.Write(Encoding.ASCII.GetBytes("Test420"));
            sdf.End();
            Console.WriteLine(bodyParser.IncomingBuffered);
            Console.WriteLine(Encoding.ASCII.GetString(bodyParser.Read()));
            Console.ReadKey();
        }

        static void BodyDetectionTest(string[] args)
        {
            ResponseHead request = new ResponseHead()
            {
                StatusCode = 200,
                StatusDescription = "OK",
                Headers = new HeaderCollection()
                {
                    ["Content-Length"] = "60"
                },
                Version = new Http.Primitives.HttpVersion(1, 1)
            };
            BodyType? bodyType = BodyType.TryDetectFor(request);
            Console.WriteLine(bodyType);
            Console.ReadKey();
        }

        static void GzipDecompressorTest(string[] args)
        {
            DeflateCompressor compressor = new DeflateCompressor(CompressionLevel.Optimal);
            DeflateDecompressor decompressor = new DeflateDecompressor();
            compressor.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            compressor.Finish();
            decompressor.Write(compressor.Read());
            compressor.End();
            decompressor.Finish();
            byte[] b = decompressor.Read();
            Console.WriteLine(b.ToBase16String());
            Console.ReadKey();
        }

        static void GzipCompressorTest(string[] args)
        {
            GzipCompressor compressor = new GzipCompressor(CompressionLevel.Optimal);
            MemoryStream m1 = new MemoryStream();
            GZipStream m2 = new GZipStream(m1, CompressionMode.Decompress);
            compressor.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            compressor.Finish();
            byte[] b = compressor.Read();
            compressor.End();
            m1.Write(b, 0, b.Length);
            b = new byte[20];
            m1.Position = 0;
            Console.WriteLine(m2.Read(b, 0, b.Length));
            Console.WriteLine(b.ToBase16String());
            Console.ReadKey();
        }

        static void ResponseSerializerTest(string[] args)
        {
            ResponseHeadSerializer serializer = new ResponseHeadSerializer();
            ResponseHeadParser parser = new ResponseHeadParser();
            serializer.Pipe(parser);
            ResponseHead head = new ResponseHead
            {
                // imaginary status
                StatusCode = 239,
                StatusDescription = "Probably Processed",
                Version = new Http.Primitives.HttpVersion(1, 1)
            };
            head.Headers.Set("probability", "0.7");
            head.Headers.Set("app_expect", "200");
            head.Headers.Set("app_expect", "400");
            serializer.Write(head);
            ResponseHead parsed = parser.Next();
            Console.ReadKey();
        }

        static void RequestSerializerTest(string[] args)
        {
            RequestHeadSerializer serializer = new RequestHeadSerializer();
            RequestHeadParser parser = new RequestHeadParser();
            serializer.Pipe(parser);
            RequestHead head = new RequestHead
            {
                Method = "GET",
                Query = new Query("/relay/servers"),
                Version = new Http.Primitives.HttpVersion(1, 1)
            };
            head.Headers.Set("host", "google.com");
            head.Headers.Set("way", "intraconnect");
            head.Headers.Set("cookie", "ga=GA.17.1.19.230148074");
            serializer.Write(head);
            RequestHead parsed = parser.Next();
            Console.ReadKey();
            serializer.End();
            parser.End();
        }

        static void RequestParserTest(string[] args)
        {
            RequestHeadParser parser = new RequestHeadParser();
            string s = "GET /teastgsdfgdrgd HTTP/1.1\r\nHost: test-host.com\r\nParamecium: Aleksa\r\n\r\n";
            byte[] data = Encoding.ASCII.GetBytes(s);
            int index = parser.WriteSafe(data);
            Console.WriteLine("{0} {1}", index, data.Length);
            Console.WriteLine(parser.Ended);
            if (!parser.Ended)
            {
                RequestHead head = parser.Next();
                Console.WriteLine("{0} {1} {2}", head.Method, head.Query, head.Version);
                for (int i = 0; i < head.Headers.Count; i++)
                    Console.WriteLine("{0}: {1}", head.Headers.GetHeaderName(i), head.Headers[i]);
                Console.WriteLine("excess: {0}", s.Substring(index));
            }
            
            Console.ReadKey();
        }

        /*static void ListTests(string[] args)
        {
            Base.List<int> test = new Base.List<int>();
            test.PushTail(10);
            test.PushTail(20);
            test.PushTail(30);
            test.PushTail(40);
            test.PushTail(50);
            Console.WriteLine(test.Join(" "));
            test.Remove(20);
            Console.WriteLine(test.Join(" "));
            test.RemoveAt(2);
            Console.WriteLine(test.Join(" "));
            test.PopTailRange(1);
            Console.WriteLine(test.Join(" "));
            int[] array = test.CopyToArray();
            Console.WriteLine(array.ToString<int>());
            Console.ReadKey();
        }*/

        static void QueryTest(string[] args)
        {
            SearchTokenList collection = new SearchTokenList();
            collection.Set("test", "123");
            Console.WriteLine(collection);
            collection.Set("test", "456");
            Console.WriteLine(collection);
            collection.Set("abc", "aniogjsdf");
            Console.WriteLine(collection);

            HttpPath path = new HttpPath("/abcd/test");
            Console.WriteLine("{0} {1} {2}", path, path.Directory, path.Entry);
            path.Initialize("/test/123");
            Console.WriteLine("{0} {1} {2}", path, path.Directory, path.Entry);
            path.Traverse("./abc");
            Console.WriteLine("{0} {1} {2}", path, path.Directory, path.Entry);
            path.Traverse("../abc");
            Console.WriteLine("{0} {1} {2}", path, path.Directory, path.Entry);
            if (path.TryTraverse("../test/../abc"))
                Console.WriteLine("Late relative path passed?");
            else Console.WriteLine("Late relative path did not pass");

            Query query = new Query("/test?hash=147198#valued");
            Console.WriteLine("{0} {1} {2}", query.Path, query.Searches, query.Hash);
            Console.ReadKey();
        }

        static void HeadersTest(string[] args)
        {
            // c# impresses me every time
            HeaderCollection headers = new HeaderCollection
            {
                ["Date"] = "Test",
                ["Pebnis"] = 1.ToString()
            };
            Console.ReadKey();
        }

        static void TcpSocketHalfOpenTest(string[] args)
        {
            TcpListener listener = new TcpListener();
            TcpSocket client = new TcpSocket(true);
            TcpSocket server = null;
            listener.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            listener.OnConnection += (_server) =>
            {
                server = _server;
                Console.WriteLine("SERVER OPEN");
                server.OnData += (data) =>
                {
                    Console.WriteLine("SERVER {0}", data.ToBase16String());
                    server.End();
                };
                server.OnClose += () => Console.WriteLine("SERVER CLOSED");
            };
            listener.Start();

            client.OnOpen += () =>
            {
                Console.WriteLine("CLIENT OPEN");
                client.Write(new byte[] { 1, 2, 3, 4, 5 });
                client.Cork();
            };
            client.OnData += (data) => Console.WriteLine("CLIENT {0}", data.ToBase16String());
            client.OnError += (e) => Console.WriteLine("CLIENT ERROR {0}", e);
            client.OnClose += () => Console.WriteLine("CLIENT CLOSED");
            client.OnEnd += () =>
            {
                Console.WriteLine("CLIENT HALF-CLOSED");
                client.End();
            };
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            Console.ReadKey();
        }

        static void TcpSocketTest(string[] args)
        {
            TcpListener listener = new TcpListener();
            TcpSocket client = new TcpSocket();
            TcpSocket server = null;
            listener.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            listener.OnConnection += (_server) =>
            {
                server = _server;
                Console.WriteLine("SERVER OPEN");
                server.OnData += (data) =>
                {
                    Console.WriteLine("SERVER {0}", data.ToBase16String());
                    if (server.State == TcpSocketState.Open) server.End();
                };
                server.OnClose += () => Console.WriteLine("SERVER CLOSED");
            };
            listener.Start();

            client.OnOpen += () =>
            {
                Console.WriteLine("CLIENT OPEN");
                client.Write(new byte[] { 1, 2, 3, 4, 5 });
            };
            client.OnData += (data) => Console.WriteLine("CLIENT {0}", data.ToBase16String());
            client.OnError += (e) => Console.WriteLine("CLIENT ERROR {0}", e);
            client.OnClose += () => Console.WriteLine("CLIENT CLOSED");
            client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 420));
            Console.ReadKey();
        }
    }

    static class Extensions
    {
        public static string ToBase16String(this byte[] array)
        {
            string s = "";
            for (long i = 0; i < array.LongLength; i++)
                s += array[i].ToString("X2");
            return s;
        }

        public static string ToString<T>(this IEnumerable<T> array)
        {
            string s = "";
            foreach (T item in array) s += item.ToString() + ", ";
            if (s.Length == 0) return s;
            return s.Remove(s.Length - 2, 2);
        }

        public static string ToString<T>(this IEnumerable<T> array, Func<T, string> converter)
        {
            string s = "";
            foreach (T item in array) s += converter(item) + ", ";
            if (s.Length == 0) return s;
            return s.Remove(s.Length - 2, 2);
        }
    }
}