using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Comet.Tickets;

public static class TicketKeys
{
    public static (string PrivatePem, string PublicPem) Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportPkcs8PrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    public static string KeyId(ECDsa key) => Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo())[..8]);

    public static ECDsaSecurityKey PrivateKey(string pem) => Load(pem, needsPrivate: true);

    public static ECDsaSecurityKey PublicKey(string pem) => Load(pem, needsPrivate: false);

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
