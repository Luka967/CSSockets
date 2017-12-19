using System;

namespace CSSockets.Http.Primitives
{
    sealed public class HttpVersion
    {
        public byte Major { get; set; }
        public byte Minor { get; set; }

        public static bool TryParse(string str, out HttpVersion result)
        {
            result = null;
            if (str.Length < 5 || !str.StartsWith("HTTP/"))
                return false;
            str = str.Substring(5);
            string[] split = str.Split(".");
            if (split.Length != 2)
                return false;
            if (!byte.TryParse(split[0], out byte _1))
                return false;
            if (!byte.TryParse(split[1], out byte _2))
                return false;
            result = new HttpVersion(_1, _2);
            return true;
        }
        public static HttpVersion Parse(string str)
        {
            if (!TryParse(str, out HttpVersion result))
                throw new ArgumentException("Invalid string format");
            return result;
        }
       
        public HttpVersion(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
        }

        public override string ToString()
            => "HTTP/" + Major + "." + Minor;

        public static implicit operator System.Version(HttpVersion version)
            => new System.Version(version.Major, version.Minor);
        public static implicit operator HttpVersion(System.Version version)
        {
            if (version.Revision != 0 || version.Build != 0 ||
                version.Major < 0 || version.Minor < 0 ||
                version.Major > 255 || version.Minor > 255)
                throw new InvalidOperationException("Cannot convert version " + version + " to an HTTP version");
            return new HttpVersion((byte)version.Major, (byte)version.Minor);
        }
    }
}
