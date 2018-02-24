using System;
using CSSockets.Streams;
using CSSockets.Http.Base;
using CSSockets.Http.Structures;

namespace CSSockets.Http.Reference
{
    public class ResponseHead : Head
    {
        public ushort? StatusCode { get; set; } = 200;
        public string StatusDescription { get; set; } = "OK";
    }

    public class ResponseHeadParser : HeadParser<ResponseHead>
    {
        private enum ParserState : byte
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
        private ParserState state = ParserState.Version;

        protected override bool TryContinue(byte[] data)
        {
            bool run = true;
            ulong i = 0, l = (ulong)data.LongLength;
            for (; i < l && run; i++)
            {
                char c = (char)data[i];
                switch (state)
                {
                    case ParserState.Version:
                        if (c != WHITESPACE) CsQueue.Append(c);
                        else
                        {
                            if (!Structures.Version.TryParse(CsQueue.Next(), out Structures.Version result))
                                return (Malformed = true) && !End();
                            Current.Version = result;
                            CsQueue.New();
                            state = ParserState.StatusCode;
                        }
                        break;
                    case ParserState.StatusCode:
                        if (c != WHITESPACE) CsQueue.Append(c);
                        else
                        {
                            if (!ushort.TryParse(CsQueue.Next(), out ushort result))
                                return (Malformed = true) && !End();
                            Current.StatusCode = result;
                            CsQueue.New();
                            state = ParserState.StatusDescription;
                        }
                        break;
                    case ParserState.StatusDescription:
                        if (c != CR) CsQueue.Append(c);
                        else
                        {
                            Current.StatusDescription = CsQueue.Next();
                            CsQueue.New();
                            state = ParserState.FirstLf;
                        }
                        break;
                    case ParserState.FirstLf:
                        if (c != LF)
                            return (Malformed = true) && !End();
                        state = ParserState.HeaderName;
                        break;
                    case ParserState.HeaderName:
                        if (c == CR) state = ParserState.Lf;
                        else if (c != COLON) CsQueue.Append(c);
                        else
                        {
                            CsQueue.New();
                            state = ParserState.HeaderValue;
                        }
                        break;
                    case ParserState.HeaderValue:
                        if (c != CR) CsQueue.Append(c);
                        else
                        {
                            Current.Headers.Set(CsQueue.Next(), CsQueue.Next().Trim());
                            state = ParserState.HeaderLf;
                        }
                        break;
                    case ParserState.HeaderLf:
                        if (c != LF)
                            return (Malformed = true) && !End();
                        else
                        {
                            CsQueue.New();
                            state = ParserState.HeaderName;
                        }
                        break;
                    case ParserState.Lf:
                        if (c != LF)
                            return (Malformed = true) && !End();
                        run = false;
                        Push();
                        state = ParserState.Version;
                        break;
                }
            }
            Bhandle(PrimitiveBuffer.Slice(data, i, l));
            return true;
        }
    }
    public class ResponseHeadSerializer : HeadSerializer<ResponseHead>
    {
        protected override string Stringify(ResponseHead head)
        {
            string s = (head.Version.ToString() ?? throw new ArgumentException("Version cannot be null")) + WHITESPACE +
                (head.StatusCode ?? throw new ArgumentException("StatusCode cannot be null")) + WHITESPACE +
                (head.StatusDescription?.ToString() ?? throw new ArgumentException("StatusDescription cannot be null")) + CR + LF;
            if (head.Headers == null) throw new ArgumentException("Headers cannot be null");
            foreach (Header h in head.Headers) s += h.Name + COLON + WHITESPACE + h.Value + CR + LF;
            return s + CR + LF;
        }
    }
}
