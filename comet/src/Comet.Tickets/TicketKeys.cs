using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Comet.Tickets;

/// <summary>
/// ECDSA P-256 keys for join tickets, as PEM files. A key's ID is the first 8 bytes of its public key's SHA-256 (in
/// hex), so it can't be mislabeled. The Login server signs with one private key; game servers accept every public key
/// in a folder, so old and new keys both work during a rotation.
/// </summary>
public static class TicketKeys
{
    /// <summary>A new key pair, as PEM text: the private key (for the Login server) and the public key (for game servers).</summary>
    public static (string PrivatePem, string PublicPem) Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>A key's ID, from its public key.</summary>
    public static string KeyId(ECDsa key) =>
        Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo())[..8]);

    /// <summary>Loads a private key from PEM text, for signing.</summary>
    public static ECDsaSecurityKey PrivateKey(string pem) => Load(pem, needsPrivate: true);

    /// <summary>Loads a public key from PEM text, for checking.</summary>
    public static ECDsaSecurityKey PublicKey(string pem) => Load(pem, needsPrivate: false);

    /// <summary>Every public key (<c>*.pem</c>) in a folder.</summary>
    public static IReadOnlyList<ECDsaSecurityKey> PublicKeysIn(string folder) =>
        Directory.GetFiles(folder, "*.pem").Order().Select(file => PublicKey(File.ReadAllText(file))).ToList();

    private static ECDsaSecurityKey Load(string pem, bool needsPrivate)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        if (key.KeySize != 256)
        {
            throw new ArgumentException($"Ticket keys are P-256; this one is {key.KeySize} bits.");
        }

        if (needsPrivate)
        {
            try
            {
                key.ExportECPrivateKey(); // throws for a public-only key
            }
            catch (CryptographicException e)
            {
                throw new ArgumentException("This PEM has no private key.", e);
            }
        }

        return new ECDsaSecurityKey(key) { KeyId = KeyId(key) };
    }
}
