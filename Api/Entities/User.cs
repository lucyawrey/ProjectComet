namespace AzaleaGames.ProjectComet.Api.Entities;

public class User : IUpdated, IGeneratedId, IGeneratedHandle
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Handle { get; set; } = 0;
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public Role Role { get; set; } = Role.NewPlayer;

    /// <summary>
    /// The user's hashed password. This is very sensitive information and should never be sent to a client.
    /// </summary>
    public required string PasswordHash { get; set; }
    /// <summary>
    /// The user's account recovery code. This is very sensitive information and should never be sent to a client.
    /// </summary>
    public string? RecoveryCode { get; set; }
}

public enum Role
{
    NewPlayer = 0, Player = 1, MembershipPlayer = 2, GameModerator = 3, GameAdministrator = 4
}
