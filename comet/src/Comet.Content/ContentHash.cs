using System;
using System.Security.Cryptography;

namespace Comet.Content
{
    /// <summary>
    /// A short fingerprint of a content file, so tools can tell whether a running server plays the same content they
    /// have (a game server's <c>/info</c> names it).
    /// </summary>
    public static class ContentHash
    {
        /// <summary>The first 16 hex digits of the file's SHA-256, lowercase.</summary>
        public static string Of(byte[] contentFile)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(contentFile);
            return BitConverter.ToString(hash, 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
