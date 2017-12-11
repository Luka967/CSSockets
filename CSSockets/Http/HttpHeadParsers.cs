using System;
using System.Text;
using System.Threading;
using WebSockets.Base;
using WebSockets.Streams;

namespace WebSockets.Http
{
    abstract public class HttpHeadParser : BaseDuplex { }

    internal enum RequestParserState
    {
        Method = 1,
        Query = 2,
        Version = 3,
        FirstLf = 4,
        HeaderName = 5,
        HeaderValue = 6,
        HeaderLf = 7,
        Lf = 8
    }
    public class HttpRequestHeadParser : HttpHeadParser, IAsyncOutputter<HttpRequestHead>
    {
        public event AsyncCreationHandler<HttpRequestHead> OnOutput;
        private Queue<HttpRequestHead> HeadQueue { get; } = new Queue<HttpRequestHead>();
        private void PushIncoming()
        {
            if (OnOutput != null) OnOutput(Incoming);
            else HeadQueue.Enqueue(Incoming);
            Incoming = new HttpRequestHead();
        }
        public HttpRequestHead Next()
        {
            ThrowIfEnded();
            if (!HeadQueue.Dequeue(out HttpRequestHead item))
                // ended
                return null;
            return item;
        }

        private const char WHITESPACE = ' ';
        private const char EQUALS = '=';
        private const char CR = '\r';
        private const char LF = '\n';
        private HttpRequestHead Incoming { get; set; } = new HttpRequestHead();
        private RequestParserState State { get; set; } = RequestParserState.Method;
        private HttpStringQueue StringQueue { get; } = new HttpStringQueue();
        private int ProcessData(byte[] data, bool writeExcess)
        {
            ThrowIfEnded();
            bool run = true; int i = 0;
            for (; i < data.Length && run; i++)
            {
                char c = (char)data[i];
                switch (State)
                {
                    case RequestParserState.Method:
                        if (c != WHITESPACE) StringQueue.Append(c);
                        else
                        {
                            Incoming.Method = StringQueue.Next();
                            StringQueue.New();
                            State = RequestParserState.Query;
                        }
                        break;
                    case RequestParserState.Query:
                        if (c != WHITESPACE) StringQueue.Append(c);
                        else
                        {
                            if (!HttpQuery.TryParse(StringQueue.Next(), out HttpQuery result))
                            {
                                End();
                                return -1;
                            }
                            Incoming.Query = result;
                            StringQueue.New();
                            State = RequestParserState.Version;
                        }
                        break;
                    case RequestParserState.Version:
                        if (c != CR) StringQueue.Append(c);
                        else
                        {
                            if (!HttpVersion.TryParse(StringQueue.Next(), out HttpVersion result))
                            {
                                End();
                                return -1;
                            }
                            Incoming.Version = result;
                            StringQueue.New();
                            State = RequestParserState.FirstLf;
                        }
                        break;
                    case RequestParserState.FirstLf:
                        if (c != LF) { End(); return -1; }
                        State = RequestParserState.HeaderName;
                        break;
                    case RequestParserState.HeaderName:
                        if (c == CR) State = RequestParserState.Lf;
                        else if (c != EQUALS) StringQueue.Append(c);
                        else
                        {
                            StringQueue.New();
                            State = RequestParserState.HeaderValue;
                        }
                        break;
                    case RequestParserState.HeaderValue:
                        if (c != CR) StringQueue.Append(c);
                        else
                        {
                            Incoming.Headers.Set(StringQueue.Next(), StringQueue.Next().Trim());
                            State = RequestParserState.HeaderLf;
                        }
                        break;
                    case RequestParserState.HeaderLf:
                        if (c != LF) { End(); return -1; }
                        else
                        {
                            StringQueue.New();
                            State = RequestParserState.HeaderName;
                        }
                        break;
                    case RequestParserState.Lf:
                        if (c != LF) { End(); return -1; }
                        run = false;
                        PushIncoming();
                        State = RequestParserState.Method;
                        break;
                }
            }
            if (writeExcess)
                Readable.Write(data, i, data.Length - i);
            return i;
        }

        public override byte[] Read() => Readable.Read();
        public override byte[] Read(int length) => Readable.Read(length);
        public override void Write(byte[] data) => ProcessData(data, true);
        public int WriteWithOverflow(byte[] data) => ProcessData(data, false);

        public override void End()
        {
            base.End();
            HeadQueue.End();
        }
    }
}
