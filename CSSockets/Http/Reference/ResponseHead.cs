using CSSockets.Streams;
using CSSockets.Http.Definition;

namespace CSSockets.Http.Reference
{
    public sealed class ResponseHead : Head
    {
        public ushort StatusCode { get; set; }
        public string StatusDescription { get; set; }

        public ResponseHead() : base() { }
        public ResponseHead(Version version, ushort statusCode, string statusDescription) : base(version)
        {
            StatusCode = statusCode;
            StatusDescription = statusDescription;
        }
        public ResponseHead(Version version, ushort statusCode, string statusDescription, HeaderCollection headers) : base(version, headers)
        {
            StatusCode = statusCode;
            StatusDescription = statusDescription;
        }
    }
    public sealed class ResponseHeadSerializer : HeadSerializer<ResponseHead>
    {
        public override bool Write(ResponseHead source)
        {
            string stringified = source.Version + WS + source.StatusCode + WS + source.StatusDescription + CRLF;
            for (int i = 0; i < source.Headers.Length; i++)
                stringified += source.Headers[i].Key.ToLower() + COLON + WS + source.Headers[i].Value + CRLF;
            stringified += CRLF;
            return HandleReadable(System.Text.Encoding.ASCII.GetBytes(stringified));
        }
    }
    public sealed class ResponseHeadParser : HeadParser<ResponseHead>
    {
        private enum ParseState : byte
        {
            Version,
            StatusCode,
            StatusDescription,
            FirstLf,
            HeaderName,
            HeaderValue,
            HeaderLf,
            Lf
        }

        private ParseState State = ParseState.Version;
        public bool Malformed { get; private set; } = false;

        public ResponseHeadParser() => Reset();

        private string IncomingVersion;
        private Version IncomingVersionValue;
        private string IncomingStatusCode;
        private string IncomingStatusDescription;
        private HeaderCollection IncomingHeaders;
        private string IncomingHeaderName;
        private string IncomingHeaderValue;

        public bool Reset()
        {
            lock (Sync)
            {
                Malformed = false;
                State = ParseState.Version;
                IncomingVersion = "";
                IncomingVersionValue = default(Version);
                IncomingStatusCode = "";
                IncomingStatusDescription = "";
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
                    case ParseState.Version:
                        if (c != WS) IncomingVersion += c;
                        else
                        {
                            if (!Version.TryParse(IncomingVersion, out Version result))
                                return !(Malformed = true);
                            IncomingVersionValue = result;
                            State = ParseState.StatusCode;
                        }
                        break;
                    case ParseState.StatusCode:
                        if (c == WS) State = ParseState.StatusDescription;
                        else if (char.IsDigit(c)) IncomingStatusCode += c;
                        else return !(Malformed = true);
                        break;
                    case ParseState.StatusDescription:
                        if (c != CR) IncomingStatusDescription += c;
                        else State = ParseState.FirstLf;
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
                        Pickup(new ResponseHead(IncomingVersionValue, ushort.Parse(IncomingStatusCode), IncomingStatusDescription, IncomingHeaders));
                        return Reset();
                }
            }
            return true;
        }
    }
}
