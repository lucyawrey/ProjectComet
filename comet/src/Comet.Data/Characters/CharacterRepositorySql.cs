namespace Comet.Data.Characters;

/// <summary>
/// The character repository's hand-written SQL: only what EF Core can't express (an upsert that returns a value,
/// batched writes over arrays, row locks). Everything else goes through EF. Each statement is covered by the
/// repository's contract tests on a real PostgreSQL.
/// </summary>
internal static class CharacterRepositorySql
{
    private const string Character = $"{CometRegion.Schema}.{CometRegion.CharacterTable}";
    private const string Owner = $"{CometRegion.Schema}.{CometRegion.CharacterOwnerTable}";
    private const string Status = $"{CometRegion.Schema}.{CometRegion.CharacterStatusTable}";

    /// <summary>
    /// Claims a live character for <c>@server</c>: makes its owner row or takes it over, adding one to the version,
    /// and returns the new version (no row if the character is missing or deleted). EF can't upsert and return the
    /// result in one statement, and the read-then-write it would need races another claim.
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
    /// Writes a batch of status saves in one statement, each only while <c>@server</c> still holds the character
    /// under the save's version, and returns the characters whose saves applied. The owner rows are share-locked,
    /// so a claim committing meanwhile is seen (and wins). EF can't upsert, or write a batch in one statement.
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
    /// Locks a character's owner row for the rest of the transaction and reads who holds it, so a look save or a
    /// leave checks the version and writes before any claim can change it. EF has no row locks.
    /// </summary>
    public const string LockOwner = $"""
        SELECT server_id, version, released_at IS NULL
        FROM {Owner}
        WHERE character_id = @character
        FOR UPDATE
        """;
}
