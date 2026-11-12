using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Comet.Tickets;

/// <summary>ES256 JWTs: <c>sub</c> is the account, <c>chr</c> the character, <c>aud</c> the game server's node, <c>jti</c> a one-time ID.</summary>
public sealed class TicketIssuer(ECDsaSecurityKey privateKey, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly SigningCredentials _credentials = new(privateKey, SecurityAlgorithms.EcdsaSha256);
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string Issue(long accountId, long characterId, int serverId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = TicketClaims.Issuer,
                Audience = serverId.ToString(),
                Subject = new ClaimsIdentity([
                    new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                    new Claim(TicketClaims.Character, characterId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                ]),
                IssuedAt = now,
                NotBefore = now,
                Expires = now + Lifetime,
                SigningCredentials = _credentials,
            }
        );
    }
}
