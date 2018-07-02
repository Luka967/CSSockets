using System;
using System.Text;
using System.Security.Cryptography;

namespace CSSockets.WebSockets.Definition
{
    internal static class Secret
    {
        public static readonly SHA1 Hasher = SHA1.Create();
        public static readonly RandomNumberGenerator RNG = RandomNumberGenerator.Create();
        public static readonly string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public static byte[] GenerateMask()
        {
            byte[] key = new byte[4];
            RNG.GetBytes(key);
            return key;
        }
        public static string GenerateKey()
        {
            byte[] key = new byte[4];
            RNG.GetBytes(key);
            return Convert.ToBase64String(key);
        }
        public static string ComputeAccept(string key)
        {
            byte[] accept = Hasher.ComputeHash(Encoding.UTF8.GetBytes(key + Magic));
            return Convert.ToBase64String(accept);
        }
    }
}
