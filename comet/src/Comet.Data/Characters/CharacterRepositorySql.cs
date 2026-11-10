namespace Comet.Data.Characters;

internal static class CharacterRepositorySql
{
    private const string Character = $"{CometRegion.Schema}.{CometRegion.CharacterTable}";
    private const string Owner = $"{CometRegion.Schema}.{CometRegion.CharacterOwnerTable}";
    private const string Status = $"{CometRegion.Schema}.{CometRegion.CharacterStatusTable}";

    /// <summary>
    /// Returns the new owner version, no row if the character is missing or deleted. A read-then-write in EF would
    /// race another claim.
    /// </summary>
    public const string Claim = $"""
        INSERT INTO {Owner} AS o (character_id, server_id, version, created_at, updated_at, released_at)
        SELECT c.id, @server, 1, @now, @now, NULL
        FROM {Character} c
        WHERE c.id = @character AND c.deleted_at IS NULL
        ON CONFLICT (character_id) DO UPDATE
        SET server_id = excluded.server_id, version = o.version + 1, updated_at = excluded.updated_at, released_at = NULL
        RETURNING o.version
        """;

    /// <summary>
    /// Returns the characters whose saves applied. The owner rows are share-locked, so a claim committing meanwhile
    /// is seen (and wins).
    /// </summary>
    public const string SaveStatuses = $"""
        WITH save AS (
            SELECT * FROM unnest(@characters, @versions, @zones, @xs, @ys, @zs, @facings)
                AS s(character_id, version, zone, x, y, z, facing)
        ), held AS (
            SELECT o.character_id
            FROM {Owner} o
            JOIN save s ON s.character_id = o.character_id
            WHERE o.version = s.version AND o.server_id = @server AND o.released_at IS NULL
            FOR SHARE OF o
        )
        INSERT INTO {Status} AS cs (character_id, zone, x, y, z, facing, created_at, updated_at)
        SELECT s.character_id, s.zone, s.x, s.y, s.z, s.facing, @now, @now
        FROM save s
        JOIN held h ON h.character_id = s.character_id
        ON CONFLICT (character_id) DO UPDATE
        SET zone = excluded.zone, x = excluded.x, y = excluded.y, z = excluded.z, facing = excluded.facing,
            updated_at = excluded.updated_at
        RETURNING cs.character_id
        """;

    /// <summary>
    /// Holds the lock to the end of the transaction, so a look save or leave checks the version and writes before any
    /// claim can change it.
    /// </summary>
    public const string LockOwner = $"""
        SELECT server_id, version, released_at IS NULL
        FROM {Owner}
        WHERE character_id = @character
        FOR UPDATE
        """;
}
