namespace Comet.Data.Accounts;

/// <summary>What an account is for. Staff powers live on separate staff accounts. Stored as a number.</summary>
public enum AccountKind : short
{
    Player = 1,
    Staff = 2,
}
