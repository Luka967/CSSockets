using System;
using System.IO;
using System.Net;
using System.Text;
using CSSockets.Tcp;
using CSSockets.Http;
using CSSockets.Base;
using System.Threading;
using CSSockets.Streams;
using System.IO.Compression;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace CSSockets
{
    class Program
    {
        static void Main(string[] args)
        {
            RequestBodyParseTest(args);
        }

        static void RequestBodyParseTest(string[] args)
        {
            // binary uncompressed
            HttpRequestHead request = new HttpRequestHead()
            {
                Method = "POST",
                Headers = new HeaderCollection()
                {
                    ["Transfer-Encoding"] = "chunked, deflate",
                },
                Query = new Query("/"),
                Version = new Http.Version(1, 1)
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
            HttpRequestHead request = new HttpRequestHead()
            {
                Method = "GET",
                Headers = new HeaderCollection()
                {
                    new Header("Transfer-Encoding", "chunked"),
                },
                Query = new Query("/"),
                Version = new Http.Version(1, 1)
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
            HttpRequestHead request = new HttpRequestHead()
            {
                Method = "GET",
                Headers = new HeaderCollection()
                {
                    ["Content-Length"] = "9",
                    ["Content-Encoding"] = "deflate"
                },
                Query = new Query("/"),
                Version = new Http.Version(1, 1)
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
            HttpResponseHead request = new HttpResponseHead()
            {
                StatusCode = 200,
                StatusDescription = "OK",
                Headers = new HeaderCollection()
                {
                    ["Content-Length"] = "60"
                },
                Version = new Http.Version(1, 1)
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
            HttpResponseHead head = new HttpResponseHead
            {
                // imaginary status
                StatusCode = 239,
                StatusDescription = "Probably Processed",
                Version = new Http.Version(1, 1)
            };
            head.Headers.Set("probability", "0.7");
            head.Headers.Set("app_expect", "200");
            head.Headers.Set("app_expect", "400");
            serializer.Write(head);
            HttpResponseHead parsed = parser.Next();
            Console.ReadKey();
        }

        static void RequestSerializerTest(string[] args)
        {
            RequestHeadSerializer serializer = new RequestHeadSerializer();
            RequestHeadParser parser = new RequestHeadParser();
            serializer.Pipe(parser);
            HttpRequestHead head = new HttpRequestHead
            {
                Method = "GET",
                Query = new Query("/relay/servers"),
                Version = new Http.Version(1, 1)
            };
            head.Headers.Set("host", "google.com");
            head.Headers.Set("way", "intraconnect");
            head.Headers.Set("cookie", "ga=GA.17.1.19.230148074");
            serializer.Write(head);
            HttpRequestHead parsed = parser.Next();
            Console.ReadKey();
            serializer.End();
            parser.End();
        }

        static void RequestParserTest(string[] args)
        {
            RequestHeadParser parser = new RequestHeadParser();
            string s =
@"GET /teastgsdfgdrgd rg HTTP/1.1
Host: test-host.com
Paramecium: Aleksa
";
            byte[] data = Encoding.ASCII.GetBytes(s);
            Console.WriteLine("{0} {1}", parser.WriteSafe(data), data.Length);
            Console.WriteLine(parser.Ended);
            if (!parser.Ended)
            {
                HttpRequestHead head = parser.Next();
                Console.WriteLine("{0} {1} {2}", head.Method, head.Query, head.Version);
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

            Http.Path path = new Http.Path("/abcd/test");
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