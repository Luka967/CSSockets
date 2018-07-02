using System;

namespace CSSockets.Http.Definition
{
    public struct Version
    {
        public byte Major { get; }
        public byte Minor { get; }

        public static bool TryParse(string str, out Version result)
        {
            result = default(Version);
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
            if (version.Revision != -1 || version.Build != -1 ||
                version.Major < 0 || version.Minor < 0 ||
                version.Major > 255 || version.Minor > 255)
                throw new InvalidOperationException("Cannot convert version " + version + " to an HTTP version");
            return new Version((byte)version.Major, (byte)version.Minor);
        }
        public static implicit operator Version(string str) => Parse(str);
        public static implicit operator string(Version version) => version.ToString();

        public static bool operator ==(Version a, Version b) => a.Major == b.Major && a.Minor == b.Minor;
        public static bool operator !=(Version a, Version b) => a.Major != b.Major || a.Minor != b.Minor;
        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (!(obj is Version)) return false;
            Version actual = (Version)obj;
            return Major == actual.Major && Minor == actual.Minor;
        }
        public override int GetHashCode() => Major * 256 + Minor;
    }
}
