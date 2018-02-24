using System;
using CSSockets.Http.Base;
using CSSockets.Http.Structures;
using CSSockets.Streams;

namespace CSSockets.Http.Reference
{
    public class RequestHead : Head
    {
        private string _Method;
        public string Method { get => _Method; set => _Method = value.ToUpperInvariant(); }
        public Query Query { get; set; }
    }

    public class RequestHeadParser : HeadParser<RequestHead>
    {
        private enum ParseState : byte
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
        private ParseState state = ParseState.Method;

        protected override bool TryContinue(byte[] data)
        {
            bool run = true;
            ulong i = 0, l = (ulong)data.LongLength;
            for (; i < l && run; i++)
            {
                char c = (char)data[i];
                switch (state)
                {
                    case ParseState.Method:
                        if (c != WHITESPACE) CsQueue.Append(c);
                        else
                        {
                            Current.Method = CsQueue.Next();
                            CsQueue.New();
                            state = ParseState.Query;
                        }
                        break;
                    case ParseState.Query:
                        if (c != WHITESPACE) CsQueue.Append(c);
                        else
                        {
                            if (!Query.TryParse(CsQueue.Next(), out Query result))
                                return (Malformed = true) && !End();
                            Current.Query = result;
                            CsQueue.New();
                            state = ParseState.Version;
                        }
                        break;
                    case ParseState.Version:
                        if (c != CR) CsQueue.Append(c);
                        else
                        {
                            if (!Structures.Version.TryParse(CsQueue.Next(), out Structures.Version result))
                                return (Malformed = true) && !End();
                            Current.Version = result;
                            CsQueue.New();
                            state = ParseState.FirstLf;
                        }
                        break;
                    case ParseState.FirstLf:
                        if (c != LF) return (Malformed = true) && !End();
                        state = ParseState.HeaderName;
                        break;
                    case ParseState.HeaderName:
                        if (c == CR) state = ParseState.Lf;
                        else if (c != COLON) CsQueue.Append(c);
                        else
                        {
                            CsQueue.New();
                            state = ParseState.HeaderValue;
                        }
                        break;
                    case ParseState.HeaderValue:
                        if (c != CR) CsQueue.Append(c);
                        else
                        {
                            Current.Headers.Set(CsQueue.Next(), CsQueue.Next().Trim());
                            state = ParseState.HeaderLf;
                        }
                        break;
                    case ParseState.HeaderLf:
                        if (c != LF) return (Malformed = true) && !End();
                        else
                        {
                            CsQueue.New();
                            state = ParseState.HeaderName;
                        }
                        break;
                    case ParseState.Lf:
                        if (c != LF) return (Malformed = true) && !End();
                        run = false;
                        Push();
                        state = ParseState.Method;
                        break;
                }
            }
            Bhandle(PrimitiveBuffer.Slice(data, i, l));
            return true;
        }
    }
    public class RequestHeadSerializer : HeadSerializer<RequestHead>
    {
        protected override string Stringify(RequestHead head)
        {
            string s = (head.Method ?? throw new ArgumentException("Method cannot be null")) + WHITESPACE +
                (head.Query?.ToString() ?? throw new ArgumentException("Query cannot be null")) + WHITESPACE +
                (head.Version.ToString() ?? throw new ArgumentException("Version cannot be null")) + CR + LF;
            if (head.Headers == null) throw new ArgumentException("Headers cannot be null");
            foreach (Header h in head.Headers) s += h.Name + COLON + WHITESPACE + h.Value + CR + LF;
            return s + CR + LF;
        }
    }
}
