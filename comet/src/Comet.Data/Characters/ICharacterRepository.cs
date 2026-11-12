namespace Comet.Data.Characters;

/// <summary>Every claim bumps the owner version; a save applies only while its version holds, else <see cref="SaveRefused"/> is raised.</summary>
public interface ICharacterRepository<TCharacter> : IAsyncDisposable
    where TCharacter : Character
{
    /// <summary>Raised on the writer's thread; drop only the connection under that version, since a newer connection may hold a newer one.</summary>
    event Action<SaveRefused>? SaveRefused;

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Returns <see cref="ClaimOutcome.Unavailable"/> if the database can't finish before <paramref name="cancellationToken"/>.</summary>
    Task<CharacterClaim<TCharacter>> ClaimAsync(long characterId, CancellationToken cancellationToken);

    void SaveStatus(long characterId, long version, CharacterPosition position);

    /// <summary>Only the game's own columns, never the name or deletion; the character is copied at once, so the caller may keep using it.</summary>
    void SaveCharacter(TCharacter character, long version);

    Task LeaveAsync(long characterId, long version, CharacterPosition? position);

    /// <summary>Null if the character is gone.</summary>
    Task<string?> LoadNameAsync(long characterId, CancellationToken cancellationToken);

    Task FlushAsync();

    Task StopAsync(TimeSpan timeout);
}

public readonly record struct CharacterPosition(int Zone, float X, float Y, float Z, float Facing);

public readonly record struct SaveRefused(long CharacterId, long Version);

public enum ClaimOutcome
{
    Claimed,

    /// <summary>No such character, or it's deleted.</summary>
    NotFound,

    Unavailable,
}

/// <summary>Set when <see cref="ClaimOutcome.Claimed"/>; <c>Position</c> is null before the first save; <c>Version</c> goes with saves.</summary>
public sealed record CharacterClaim<TCharacter>(
    ClaimOutcome Outcome,
    TCharacter? Character = null,
    CharacterPosition? Position = null,
    long Version = 0
)
    where TCharacter : Character;

public sealed record CharacterRepositoryOptions
{
    /// <summary>The node number its Snowflake IDs use.</summary>
    public required int ServerId { get; init; }

    public TimeSpan SaveInterval { get; init; } = TimeSpan.FromSeconds(60);
}
