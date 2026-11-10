namespace Comet.Data.Characters;

/// <summary>
/// Every claim adds one to the character's owner version. A save applies only while the version it was made under
/// still holds, so a save from an older connection or another server changes nothing and raises
/// <see cref="SaveRefused"/>.
/// </summary>
/// <remarks>
/// Saves are queued and written in order off the caller's thread. Status saves are batched per save interval, the
/// newest per character winning. Each interval the writer also checks every held version, so a character claimed
/// elsewhere or kicked by the Login server raises <see cref="SaveRefused"/> even when its player stands still.
/// </remarks>
public interface ICharacterRepository<TCharacter> : IAsyncDisposable
    where TCharacter : Character
{
    /// <summary>
    /// Raised on the writer's thread. The game drops only the connection playing that character under that version:
    /// a newer connection may hold a newer one.
    /// </summary>
    event Action<SaveRefused>? SaveRefused;

    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads the character after any of its own writes still queued. Returns <see cref="ClaimOutcome.Unavailable"/> if
    /// the database can't finish before <paramref name="cancellationToken"/>.
    /// </summary>
    Task<CharacterClaim<TCharacter>> ClaimAsync(long characterId, CancellationToken cancellationToken);

    void SaveStatus(long characterId, long version, CharacterPosition position);

    /// <summary>
    /// Queues a save of the game's own columns only, never the name or deletion. The character is copied at once, so
    /// the caller may keep changing it.
    /// </summary>
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

/// <summary>
/// Set only when <see cref="ClaimOutcome.Claimed"/>. <c>Position</c> is null before the first status save;
/// <c>Version</c> goes with every later save.
/// </summary>
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
