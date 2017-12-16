using System;
using System.Text;
using System.Threading;
using CSSockets.Base;
using CSSockets.Streams;

namespace CSSockets.Http
{
    abstract public class HeadParser<T> : UnifiedDuplex, IAsyncOutputter<T>
        where T : HttpHead, new()
    {
        public event AsyncCreationHandler<T> OnOutput;
        protected Queue<T> HeadQueue { get; } = new Queue<T>();
        protected void PushIncoming()
        {
            if (OnOutput != null) OnOutput(Incoming);
            else HeadQueue.Enqueue(Incoming);
            Incoming = new T();
        }
        public T Next()
        {
            ThrowIfEnded();
            if (!HeadQueue.Dequeue(out T item))
                // ended
                return null;
            return item;
        }
        protected T Incoming { get; set; } = new T();
        protected StringQueue StringQueue { get; } = new StringQueue();

        abstract protected int ProcessData(byte[] data, bool writeExcess);

        protected const char WHITESPACE = ' ';
        protected const char EQUALS = '=';
        protected const char CR = '\r';
        protected const char LF = '\n';

        public override byte[] Read() => Bread();
        public override byte[] Read(int length) => Bread(length);
        public override void Write(byte[] data) => ProcessData(data, true);
        public int WriteSafe(byte[] data) => ProcessData(data, false);

        public override void End()
        {
            base.End();
            HeadQueue.End();
        }
    }

    internal enum RequestParserState : byte
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
    public class RequestHeadParser : HeadParser<HttpRequestHead>
    {
        private RequestParserState State { get; set; } = RequestParserState.Method;

        protected override int ProcessData(byte[] data, bool writeExcess)
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
                            if (!Query.TryParse(StringQueue.Next(), out Query result))
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
                            if (!Version.TryParse(StringQueue.Next(), out Version result))
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
                Bwrite(data, i, data.Length - i);
            return i;
        }
    }

    internal enum ResponseParserState : byte
    {
        Version = 1,
        StatusCode = 2,
        StatusDescription = 3,
        FirstLf = 4,
        HeaderName = 5,
        HeaderValue = 6,
        HeaderLf = 7,
        Lf = 8
    }
    public class ResponseHeadParser : HeadParser<HttpResponseHead>
    {
        private ResponseParserState State { get; set; } = ResponseParserState.Version;

        protected override int ProcessData(byte[] data, bool writeExcess)
        {
            ThrowIfEnded();
            bool run = true; int i = 0;
            for (; i < data.Length && run; i++)
            {
                char c = (char)data[i];
                switch (State)
                {
                    case ResponseParserState.Version:
                        if (c != WHITESPACE) StringQueue.Append(c);
                        else
                        {
                            if (!Version.TryParse(StringQueue.Next(), out Version result))
                            {
                                End();
                                return -1;
                            }
                            Incoming.Version = result;
                            StringQueue.New();
                            State = ResponseParserState.StatusCode;
                        }
                        break;
                    case ResponseParserState.StatusCode:
                        if (c != WHITESPACE) StringQueue.Append(c);
                        else
                        {
                            if (!ushort.TryParse(StringQueue.Next(), out ushort result))
                            {
                                End();
                                return -1;
                            }
                            Incoming.StatusCode = result;
                            StringQueue.New();
                            State = ResponseParserState.StatusDescription;
                        }
                        break;
                    case ResponseParserState.StatusDescription:
                        if (c != CR) StringQueue.Append(c);
                        else
                        {
                            Incoming.StatusDescription = StringQueue.Next();
                            StringQueue.New();
                            State = ResponseParserState.FirstLf;
                        }
                        break;
                    case ResponseParserState.FirstLf:
                        if (c != LF) { End(); return -1; }
                        State = ResponseParserState.HeaderName;
                        break;
                    case ResponseParserState.HeaderName:
                        if (c == CR) State = ResponseParserState.Lf;
                        else if (c != EQUALS) StringQueue.Append(c);
                        else
                        {
                            StringQueue.New();
                            State = ResponseParserState.HeaderValue;
                        }
                        break;
                    case ResponseParserState.HeaderValue:
                        if (c != CR) StringQueue.Append(c);
                        else
                        {
                            Incoming.Headers.Set(StringQueue.Next(), StringQueue.Next().Trim());
                            State = ResponseParserState.HeaderLf;
                        }
                        break;
                    case ResponseParserState.HeaderLf:
                        if (c != LF) { End(); return -1; }
                        else
                        {
                            StringQueue.New();
                            State = ResponseParserState.HeaderName;
                        }
                        break;
                    case ResponseParserState.Lf:
                        if (c != LF) { End(); return -1; }
                        run = false;
                        PushIncoming();
                        State = ResponseParserState.Version;
                        break;
                }
            }
            if (writeExcess)
                Bwrite(data, i, data.Length - i);
            return i;
        }
    }
}
