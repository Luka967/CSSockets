using CSSockets.Http.Base;
using CSSockets.Http.Primitives;

namespace CSSockets.Http.Reference
{
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
    public class ResponseHeadParser : HeadParser<ResponseHead>
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
                            if (!HttpVersion.TryParse(StringQueue.Next(), out HttpVersion result))
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
                        else if (c != COLON) StringQueue.Append(c);
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
