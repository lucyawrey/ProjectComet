namespace AzaleaGames.ProjectComet.Api.Entities;

public class User : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required long Handle { get; set; }
    public required string Username { get; set; }
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
