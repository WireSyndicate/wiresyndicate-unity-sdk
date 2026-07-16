using System;
using System.Security.Cryptography;
using System.Text;

namespace WireSyndicate.SDK
{
    internal static class WSCryptography
    {
        /// <summary>
        /// Generates a mathematically secure HMAC-SHA256 signature for a given payload and secret.
        /// Outputs a lowercase hexadecimal string to match the Node.js crypto output on the Edge.
        /// </summary>
        public static string GenerateHMAC(string payload, string secret)
        {
            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

            using (HMACSHA256 hmac = new HMACSHA256(secretBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(payloadBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}
