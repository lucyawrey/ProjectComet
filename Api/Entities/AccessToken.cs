namespace AzaleaGames.ProjectComet.Api.Entities;

public class AccessToken : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required DateTime ExpiresAt { get; set; }
    public AccessLevel AccessLevel { get; set; }
    /// <summary>
    /// The hashed access token. This is very sensitive information and should never be sent to a client.
    /// Token format is: {AccessLevel}_{IdBase32}_{SecretBase32}
    /// </summary>
    public required string AccessTokenHash { get; set; }
}

public enum AccessLevel
{
    Default = 0, GameServer = 1, Administrator = 2
}
