namespace Comet.Data.Accounts;

/// <summary>Whether an account may sign in and play. Stored as a number.</summary>
public enum AccountStatus : short
{
    Active = 1,

    /// <summary>Can't sign in or get join tickets.</summary>
    Banned = 2,
}
