using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
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
    public class RequestHeadParser : HeadParser<RequestHead>
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
                        else if (c != COLON) StringQueue.Append(c);
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
            {
                int len = data.Length - i;
                byte[] sliced = new byte[len];
                System.Buffer.BlockCopy(data, i, sliced, 0, len);
                Bwrite(sliced);
            }
            return i;
        }
    }
}
