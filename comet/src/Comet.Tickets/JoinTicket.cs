namespace Comet.Tickets;

public sealed record JoinTicket(string TicketId, long AccountId, long CharacterId, int ServerId, DateTime ExpiresAt);

public enum TicketRejection
{
    Invalid,
    Expired,

    WrongServer,

    Used,
}

internal static class TicketClaims
{
    public const string Issuer = "comet-login";
    public const string Character = "chr";
}
