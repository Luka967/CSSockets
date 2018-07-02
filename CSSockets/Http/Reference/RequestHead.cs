using CSSockets.Streams;
using CSSockets.Http.Definition;

namespace CSSockets.Http.Reference
{
    public sealed class RequestHead : Head
    {
        public string Method { get; set; }
        public URL URL { get; set; }

        public RequestHead() : base() { }
        public RequestHead(Version version, string method, URL query) : base(version)
        {
            Method = method;
            URL = query;
        }
        public RequestHead(Version version, string method, URL query, HeaderCollection headers) : base(version, headers)
        {
            Method = method;
            URL = query;
        }
    }

    public sealed class RequestHeadSerializer : HeadSerializer<RequestHead>
    {
        public override bool Write(RequestHead source)
        {
            string stringified = source.Method + WS + source.URL + WS + source.Version + CRLF;
            for (int i = 0; i < source.Headers.Length; i++)
                stringified += source.Headers[i].Key.ToLower() + COLON + WS + source.Headers[i].Value + CRLF;
            stringified += CRLF;
            return HandleReadable(System.Text.Encoding.ASCII.GetBytes(stringified));
        }
    }
    public sealed class RequestHeadParser : HeadParser<RequestHead>
    {
        private enum ParseState : byte
        {
            Method,
            Query,
            Version,
            FirstLf,
            HeaderName,
            HeaderValue,
            HeaderLf,
            Lf
        }

        private ParseState State = ParseState.Method;
        public bool Malformed { get; private set; } = false;

        public RequestHeadParser() => Reset();

        private string IncomingMethod;
        private string IncomingQuery;
        private string IncomingVersion;
        private Version IncomingVersionValue;
        private HeaderCollection IncomingHeaders;
        private string IncomingHeaderName;
        private string IncomingHeaderValue;

        public bool Reset()
        {
            lock (Sync)
            {
                Malformed = false;
                State = ParseState.Method;
                IncomingMethod = "";
                IncomingQuery = "";
                IncomingVersion = "";
                IncomingVersionValue = default(Version);
                IncomingHeaders = new HeaderCollection();
                IncomingHeaderName = "";
                IncomingHeaderValue = "";
                return true;
            }
        }
        protected override bool HandleWritable(byte[] source)
        {
            if (Malformed) return false;
            ulong i = 0, sourceLength = (ulong)source.LongLength;
            for (char c; i < sourceLength; i++)
            {
                c = (char)source[i];
                switch (State)
                {
                    case ParseState.Method:
                        if (c != WS) IncomingMethod += c;
                        else State = ParseState.Query;
                        break;
                    case ParseState.Query:
                        if (c != WS) IncomingQuery += c;
                        else
                        {
                            if (!URL.TryParse(IncomingQuery, out URL result))
                                return !(Malformed = true);
                            IncomingQuery = result;
                            State = ParseState.Version;
                        }
                        break;
                    case ParseState.Version:
                        if (c != CR) IncomingVersion += c;
                        else
                        {
                            if (!Version.TryParse(IncomingVersion, out Version result))
                                return !(Malformed = true);
                            IncomingVersionValue = result;
                            State = ParseState.FirstLf;
                        }
                        break;
                    case ParseState.FirstLf:
                        if (c != LF) return !(Malformed = true);
                        State = ParseState.HeaderName;
                        break;
                    case ParseState.HeaderName:
                        if (c == CR) State = ParseState.Lf;
                        else if (c != COLON) IncomingHeaderName += c;
                        else State = ParseState.HeaderValue;
                        break;
                    case ParseState.HeaderValue:
                        if (c != CR) IncomingHeaderValue += c;
                        else
                        {
                            IncomingHeaders.Add(IncomingHeaderName, IncomingHeaderValue.TrimStart());
                            IncomingHeaderName = "";
                            IncomingHeaderValue = "";
                            State = ParseState.HeaderLf;
                        }
                        break;
                    case ParseState.HeaderLf:
                        if (c != LF) return !(Malformed = true);
                        else State = ParseState.HeaderName;
                        break;
                    case ParseState.Lf:
                        if (c != LF) return !(Malformed = true);
                        HandleReadable(PrimitiveBuffer.Slice(source, i + 1, sourceLength));
                        Pickup(new RequestHead(IncomingVersionValue, IncomingMethod, IncomingQuery, IncomingHeaders));
                        return Reset();
                }
            }
            return true;
        }
    }
}
