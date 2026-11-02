namespace Comet.Data.Characters;

/// <summary>
/// A game server's access to its characters: claiming one as a player joins, saving it while they play, and
/// releasing it as they leave. Every claim adds one to the character's owner version; each save carries the version
/// it was made under and applies only while that version still holds, so a save from an older connection, or from
/// another server, changes nothing and raises <see cref="SaveRefused"/>.
/// </summary>
/// <remarks>
/// Saves are queued and written in order off the caller's thread. Status saves are batched per save interval (a newer
/// status replaces a queued older one for the same character); look saves and leaves are written as they come. Each
/// interval the writer also checks the version of every character this server holds, so a character claimed
/// elsewhere, or kicked by the Login server (which bumps the version), raises <see cref="SaveRefused"/> even when its
/// player stands still.
/// </remarks>
public interface ICharacterRepository<TCharacter> : IAsyncDisposable
    where TCharacter : Character
{
    /// <summary>
    /// Raised on the writer's thread when a save or the interval's version check finds that this server no longer
    /// holds a character under the version it was saving with. The game drops the connection playing that character
    /// under that version (and only that one: a newer connection may hold a newer version).
    /// </summary>
    event Action<SaveRefused>? SaveRefused;

    /// <summary>
    /// Waits until the database is ready (the schema the game expects, retrying while it isn't), releases every
    /// character still held under this server's ID (left over from a crash), and starts the writer.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Claims a character for this server and loads it, after any of its own writes still queued. Returns
    /// <see cref="ClaimOutcome.Unavailable"/> if the database can't finish before <paramref name="cancellationToken"/>.
    /// </summary>
    Task<CharacterClaim<TCharacter>> ClaimAsync(long characterId, CancellationToken cancellationToken);

    /// <summary>Queues a status save for the next interval's batch. Doesn't block.</summary>
    void SaveStatus(long characterId, long version, CharacterPosition position);

    /// <summary>
    /// Queues a save of the game's own columns (those its character class declares; never the name or the deletion).
    /// The character is copied at once, so the caller may keep changing it. Doesn't block.
    /// </summary>
    void SaveCharacter(TCharacter character, long version);

    /// <summary>
    /// Writes the final status (if any) and releases the character, under the version check, ahead of the next
    /// interval. A later claim of the same character waits for it.
    /// </summary>
    Task LeaveAsync(long characterId, long version, CharacterPosition? position);

    /// <summary>The character's current name (after a rename), or null if it's gone.</summary>
    Task<string?> LoadNameAsync(long characterId, CancellationToken cancellationToken);

    /// <summary>Writes this interval's status saves and checks held versions now, instead of at the interval.</summary>
    Task FlushAsync();

    /// <summary>
    /// Stops the writer after writing what's queued, giving up after <paramref name="timeout"/> and logging what was
    /// lost.
    /// </summary>
    Task StopAsync(TimeSpan timeout);
}

/// <summary>Where a character is: the zone's content number, its position and facing.</summary>
public readonly record struct CharacterPosition(int Zone, float X, float Y, float Z, float Facing);

/// <summary>A save refused because this server no longer holds the character under <see cref="Version"/>.</summary>
public readonly record struct SaveRefused(long CharacterId, long Version);

public enum ClaimOutcome
{
    Claimed,

    /// <summary>No such character, or it's deleted.</summary>
    NotFound,

    /// <summary>The database couldn't be reached in time.</summary>
    Unavailable,
}

/// <summary>
/// A claim's result: on <see cref="ClaimOutcome.Claimed"/>, the character, its saved position (null before its first
/// status save), and the owner version every later save carries.
/// </summary>
public sealed record CharacterClaim<TCharacter>(
    ClaimOutcome Outcome, TCharacter? Character = null, CharacterPosition? Position = null, long Version = 0)
    where TCharacter : Character;

/// <summary>Settings for a game server's character repository.</summary>
public sealed record CharacterRepositoryOptions
{
    /// <summary>This game server's node number (the one its Snowflake IDs use), stored as the owner's server ID.</summary>
    public required int ServerId { get; init; }

    /// <summary>
    /// How often queued status saves are written, and held versions checked. It bounds what a server crash can lose
    /// and how long a kicked player keeps playing.
    /// </summary>
    public TimeSpan SaveInterval { get; init; } = TimeSpan.FromSeconds(60);
}
