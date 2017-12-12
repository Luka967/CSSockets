using System;

namespace CSSockets.Http
{
    sealed public class Version
    {
        public byte Major { get; set; }
        public byte Minor { get; set; }

        public static bool TryParse(string str, out Version result)
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
            result = new Version(_1, _2);
            return true;
        }
        public static Version Parse(string str)
        {
            if (!TryParse(str, out Version result))
                throw new ArgumentException("Invalid string format");
            return result;
        }
       
        public Version(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
        }

        public override string ToString()
            => "HTTP/" + Major + "." + Minor;

        public static implicit operator System.Version(Version version)
            => new System.Version(version.Major, version.Minor);
        public static implicit operator Version(System.Version version)
        {
            if (version.Revision != 0 || version.Build != 0 ||
                version.Major < 0 || version.Minor < 0 ||
                version.Major > 255 || version.Minor > 255)
                throw new InvalidOperationException("Cannot convert version " + version + " to an HTTP version");
            return new Version((byte)version.Major, (byte)version.Minor);
        }
    }
}
