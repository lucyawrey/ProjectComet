using System.Security.Claims;
using System.Text;
using Comet.Tickets;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Comet.Tickets.Tests;

public class TicketTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly (string Private, string Public) Keys = TicketKeys.Create();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    private TicketIssuer Issuer() => new(TicketKeys.PrivateKey(Keys.Private), _clock);
    private TicketChecker Checker(int serverId = 7) => new([TicketKeys.PublicKey(Keys.Public)], serverId, _clock);

    [Fact]
    public async Task AGoodTicketSaysWhoJoinsAsWhom()
    {
        var token = Issuer().Issue(accountId: 123456789012345, characterId: 987654321098765, serverId: 7);
        var (ticket, rejection) = await Checker().CheckAsync(token);
        Assert.Null(rejection);
        Assert.Equal(123456789012345, ticket!.AccountId);
        Assert.Equal(987654321098765, ticket.CharacterId);
        Assert.Equal(7, ticket.ServerId);
    }

    [Fact]
    public async Task ATicketWorksOnce()
    {
        var checker = Checker();
        var token = Issuer().Issue(1, 2, 7);
        Assert.Null((await checker.CheckAsync(token)).Rejection);
        Assert.Equal(TicketRejection.Used, (await checker.CheckAsync(token)).Rejection);
    }

    [Fact]
    public async Task ATicketIsOnlyForItsServer()
    {
        var token = Issuer().Issue(1, 2, serverId: 8);
        Assert.Equal(TicketRejection.WrongServer, (await Checker(serverId: 7).CheckAsync(token)).Rejection);
    }

    [Fact]
    public async Task ATicketExpires()
    {
        var token = Issuer().Issue(1, 2, 7);
        _clock.Now += TicketIssuer.Lifetime + TicketChecker.ClockSkew + TimeSpan.FromSeconds(1);
        Assert.Equal(TicketRejection.Expired, (await Checker().CheckAsync(token)).Rejection);
    }

    [Fact]
    public async Task ATicketIssuedBeforeTheServerStartedIsRefused()
    {
        var ticket = Issuer().Issue(1, 2, 7);
        _clock.Now += TimeSpan.FromSeconds(1); // the game server restarts
        Assert.Equal(TicketRejection.Expired, (await Checker().CheckAsync(ticket)).Rejection);
    }

    [Fact]
    public async Task ClocksMayDisagreeALittle()
    {
        var checker = Checker();
        var token = Issuer().Issue(1, 2, 7);
        _clock.Now += TicketIssuer.Lifetime + TimeSpan.FromSeconds(3);
        Assert.Null((await checker.CheckAsync(token)).Rejection);
    }

    [Fact]
    public async Task ATamperedTicketIsRefused()
    {
        var token = Issuer().Issue(1, 2, 7);
        var parts = token.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1])).Replace("\"sub\":\"1\"", "\"sub\":\"9\"");
        Assert.Contains("\"sub\":\"9\"", payload);
        var forged = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync(forged)).Rejection);
    }

    [Fact]
    public async Task ATicketFromAnUnknownKeyIsRefused()
    {
        var other = TicketKeys.Create();
        var token = new TicketIssuer(TicketKeys.PrivateKey(other.PrivatePem), _clock).Issue(1, 2, 7);
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync(token)).Rejection);
    }

    [Fact]
    public async Task OldAndNewKeysBothWorkDuringARotation()
    {
        var next = TicketKeys.Create();
        var checker = new TicketChecker([TicketKeys.PublicKey(Keys.Public), TicketKeys.PublicKey(next.PublicPem)], 7, _clock);
        Assert.Null((await checker.CheckAsync(Issuer().Issue(1, 2, 7))).Rejection);
        Assert.Null((await checker.CheckAsync(new TicketIssuer(TicketKeys.PrivateKey(next.PrivatePem), _clock).Issue(1, 2, 7))).Rejection);
    }

    [Fact]
    public async Task OnlyES256IsAccepted()
    {
        // An HMAC ticket "signed" with the public key's bytes: the classic JWT algorithm-confusion forgery.
        var hmac = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Keys.Public)) { KeyId = TicketKeys.PublicKey(Keys.Public).KeyId };
        var forged = new JsonWebTokenHandler().CreateToken(Descriptor(new SigningCredentials(hmac, SecurityAlgorithms.HmacSha256)));
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync(forged)).Rejection);

        // An unsigned ticket (alg "none").
        var unsigned = new JsonWebTokenHandler().CreateToken(Descriptor(null));
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync(unsigned)).Rejection);
    }

    [Fact]
    public async Task GarbageIsRefused()
    {
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync("not a ticket")).Rejection);
        Assert.Equal(TicketRejection.Invalid, (await Checker().CheckAsync("")).Rejection);
    }

    [Fact]
    public void SigningNeedsThePrivateKey()
    {
        Assert.Throws<ArgumentException>(() => TicketKeys.PrivateKey(Keys.Public));
    }

    [Fact]
    public void KeyIdsComeFromTheKey()
    {
        Assert.Equal(TicketKeys.PrivateKey(Keys.Private).KeyId, TicketKeys.PublicKey(Keys.Public).KeyId);
        Assert.NotEqual(TicketKeys.PublicKey(Keys.Public).KeyId, TicketKeys.PublicKey(TicketKeys.Create().PublicPem).KeyId);
    }

    private SecurityTokenDescriptor Descriptor(SigningCredentials? credentials) => new()
    {
        Issuer = "comet-login",
        Audience = "7",
        Subject = new ClaimsIdentity([new Claim("sub", "1"), new Claim("chr", "2"), new Claim("jti", Guid.NewGuid().ToString("N"))]),
        IssuedAt = _clock.Now.UtcDateTime,
        NotBefore = _clock.Now.UtcDateTime,
        Expires = _clock.Now.UtcDateTime.AddSeconds(60),
        SigningCredentials = credentials,
    };
}
