using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Comet.Tickets;

/// <summary>
/// Checks join tickets for one game server: the signature (ES256 only, from a known key), the issuer, that it's for
/// this server, that it hasn't expired, and that it hasn't been used here before. Used tickets are remembered in
/// memory, so a ticket issued before this checker was made (before the server started) is refused as expired: it
/// may have been used on the server's previous run. Thread-safe.
/// </summary>
public sealed class TicketChecker
{
    /// <summary>How far the Login server's and this server's clocks may disagree.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    private readonly int _serverId;
    private readonly TimeProvider _clock;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;
    private readonly ConcurrentDictionary<string, DateTime> _used = new();
    private readonly DateTime _startedAt;
    private DateTime _nextPrune;

    public TicketChecker(IEnumerable<ECDsaSecurityKey> publicKeys, int serverId, TimeProvider clock)
    {
        _serverId = serverId;
        _clock = clock;
        var now = clock.GetUtcNow().UtcDateTime;
        _startedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)); // tickets carry whole seconds
        _parameters = new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            IssuerSigningKeys = publicKeys.ToList(),
            ValidIssuer = TicketClaims.Issuer,
            ValidateAudience = false, // checked below, to tell "for another server" apart
            ValidateLifetime = false, // checked below, against our clock
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            TryAllIssuerSigningKeys = false, // only the key the ticket names
        };
    }

    /// <summary>Checks a ticket and, if it's good, marks it used.</summary>
    public async Task<(JoinTicket? Ticket, TicketRejection? Rejection)> CheckAsync(string token)
    {
        TokenValidationResult result;
        try
        {
            result = await _handler.ValidateTokenAsync(token, _parameters);
        }
        catch (ArgumentException)
        {
            return (null, TicketRejection.Invalid);
        }

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            return (null, TicketRejection.Invalid);
        }

        if (!jwt.TryGetPayloadValue<string>(JwtRegisteredClaimNames.Sub, out var sub) || !long.TryParse(sub, out var account) ||
            !jwt.TryGetPayloadValue<string>(TicketClaims.Character, out var chr) || !long.TryParse(chr, out var character) ||
            string.IsNullOrEmpty(jwt.Id) || jwt.Audiences.Count() != 1)
        {
            return (null, TicketRejection.Invalid);
        }

        if (jwt.Audiences.Single() != _serverId.ToString())
        {
            return (null, TicketRejection.WrongServer);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (now > jwt.ValidTo + ClockSkew || now < jwt.ValidFrom - ClockSkew || jwt.IssuedAt < _startedAt)
        {
            return (null, TicketRejection.Expired);
        }

        Prune(now);
        if (!_used.TryAdd(jwt.Id, jwt.ValidTo + ClockSkew))
        {
            return (null, TicketRejection.Used);
        }

        return (new JoinTicket(jwt.Id, account, character, _serverId, jwt.ValidTo), null);
    }

    // Forgets used tickets once they've expired anyway, at most every few seconds.
    private void Prune(DateTime now)
    {
        if (now < _nextPrune)
        {
            return;
        }

        _nextPrune = now + TimeSpan.FromSeconds(10);
        foreach (var (id, expires) in _used)
        {
            if (expires < now)
            {
                _used.TryRemove(id, out _);
            }
        }
    }
}
