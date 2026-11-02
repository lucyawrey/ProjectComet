using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Comet.Login;

/// <summary>
/// Session tokens: 32 random bytes in base64url with a <c>cs_</c> prefix, so secret scanners and logs can tell them
/// apart. Only their SHA-256 is stored; a token this random can't be guessed however fast the hash.
/// </summary>
public static class SessionTokens
{
    public const string Prefix = "cs_";

    public static string New() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

/// <summary>
/// Recovery codes: 26 characters of Crockford's base32 (no I, L, O or U), about 130 bits from a cryptographic random
/// generator, shown in groups of four. Reading one ignores dashes, spaces and case, and takes I and L as 1 and O as
/// 0. Only the SHA-256 of the plain code is stored, looked up through a unique index.
/// </summary>
public static class RecoveryCodes
{
    public const int Length = 26;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int GroupSize = 4;

    /// <summary>A new code, grouped for showing (<c>ABCD-EFGH-…</c>).</summary>
    public static string New()
    {
        var code = RandomNumberGenerator.GetString(Alphabet, Length);
        return string.Join('-', code.Chunk(GroupSize).Select(group => new string(group)));
    }

    /// <summary>The plain code from what a player typed, or null if it can't be one.</summary>
    public static string? Normalize(string typed)
    {
        var code = new StringBuilder(Length);
        foreach (var c in typed)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c) switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                var other => other,
            };
            if (!Alphabet.Contains(upper) || code.Length == Length)
            {
                return null;
            }

            code.Append(upper);
        }

        return code.Length == Length ? code.ToString() : null;
    }

    /// <summary>The stored hash of a plain (normalized) code.</summary>
    public static byte[] Hash(string normalized) => SHA256.HashData(Encoding.ASCII.GetBytes(normalized));
}
