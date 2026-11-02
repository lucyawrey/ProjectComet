namespace Comet.Tickets;

/// <summary>What a checked join ticket says: who may join which game server, as which character.</summary>
public sealed record JoinTicket(string TicketId, long AccountId, long CharacterId, int ServerId, DateTime ExpiresAt);

/// <summary>Why a ticket was refused.</summary>
public enum TicketRejection
{
    /// <summary>Malformed, wrongly signed, signed by an unknown key, or another algorithm than ES256.</summary>
    Invalid,
    Expired,

    /// <summary>For another game server.</summary>
    WrongServer,

    /// <summary>Already used to join this server.</summary>
    Used,
}

/// <summary>The claim names in a ticket (a JWT).</summary>
internal static class TicketClaims
{
    public const string Issuer = "comet-login";
    public const string Character = "chr";
}
