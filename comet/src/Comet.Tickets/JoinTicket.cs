namespace Comet.Tickets;

public sealed record JoinTicket(string TicketId, long AccountId, long CharacterId, int ServerId, DateTime ExpiresAt);

public enum TicketRejection
{
    /// <summary>Malformed, wrongly signed, signed by an unknown key, or another algorithm than ES256.</summary>
    Invalid,
    Expired,

    WrongServer,

    /// <summary>Already used to join this server.</summary>
    Used,
}

internal static class TicketClaims
{
    public const string Issuer = "comet-login";
    public const string Character = "chr";
}
