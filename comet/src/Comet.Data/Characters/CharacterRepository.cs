using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Comet.Data.Characters;

/// <summary>
/// The character repository on PostgreSQL, through the game's region context. Each operation uses a fresh context
/// from <paramref name="createContext"/>. Hand-written SQL is in <see cref="CharacterRepositorySql"/>.
/// </summary>
public sealed class CharacterRepository<TCharacter, TContext>(
    Func<TContext> createContext, CharacterRepositoryOptions options, TimeProvider clock, ILogger logger)
    : QueuedCharacterRepository<TCharacter>(options, clock, logger)
    where TCharacter : Character
    where TContext : CometRegionContext<TCharacter>
{
    private static readonly TimeSpan SchemaRetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task PrepareAsync(CancellationToken cancellationToken)
    {
        // Only the Login server migrates, and either may start first: wait for it.
        var waiting = false;
        while (true)
        {
            try
            {
                await using var db = createContext();
                var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                {
                    break;
                }

                if (!waiting)
                {
                    Logger.LogInformation("Waiting for the region schema's migrations ({Pending})", string.Join(", ", pending));
                }
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                if (!waiting)
                {
                    Logger.LogInformation("Waiting for the region database: {Message}", exception.Message);
                }
            }

            waiting = true;
            await Task.Delay(SchemaRetryDelay, Clock, cancellationToken);
        }

        await using var context = createContext();
        var now = Now;
        var released = await context.CharacterOwners
            .Where(o => o.ServerId == Options.ServerId && o.ReleasedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ReleasedAt, now).SetProperty(o => o.UpdatedAt, now), cancellationToken);
        if (released > 0)
        {
            Logger.LogInformation("Released {Count} characters this server still held", released);
        }
    }

    protected override async Task<CharacterClaim<TCharacter>> ClaimCoreAsync(long characterId, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await using var claim = Command(db, CharacterRepositorySql.Claim);
        claim.Parameters.AddWithValue("character", characterId);
        claim.Parameters.AddWithValue("server", Options.ServerId);
        claim.Parameters.AddWithValue("now", Now);
        if (await claim.ExecuteScalarAsync(cancellationToken) is not long version)
        {
            return new(ClaimOutcome.NotFound);
        }

        var character = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == characterId, cancellationToken);
        var status = await db.CharacterStatuses.AsNoTracking().SingleOrDefaultAsync(s => s.CharacterId == characterId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        CharacterPosition? position = status == null ? null : new(status.Zone, status.X, status.Y, status.Z, status.Facing);
        return new(ClaimOutcome.Claimed, character, position, version);
    }

    protected override async Task<IReadOnlySet<long>> SaveStatusesCoreAsync(
        IReadOnlyList<StatusSave> saves, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        await db.Database.OpenConnectionAsync(cancellationToken);
        return await SaveStatusesAsync(db, saves, cancellationToken);
    }

    protected override async Task<bool> SaveCharacterCoreAsync(TCharacter character, long version, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockHeldAsync(db, character.Id, version, cancellationToken))
        {
            return false;
        }

        // Only the game's own columns: never the name or the deletion, which the Login server owns.
        var entry = db.Attach(character);
        foreach (var property in entry.Properties)
        {
            property.IsModified = property.Metadata.PropertyInfo?.DeclaringType != typeof(Character)
                && !property.Metadata.IsPrimaryKey();
        }

        character.UpdatedAt = Now;
        entry.Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    protected override async Task LeaveCoreAsync(
        long characterId, long version, CharacterPosition? position, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockHeldAsync(db, characterId, version, cancellationToken))
        {
            return;
        }

        if (position is { } final)
        {
            await SaveStatusesAsync(db, [new(characterId, version, final)], cancellationToken);
        }

        var now = Now;
        await db.CharacterOwners
            .Where(o => o.CharacterId == characterId)
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.ReleasedAt, now).SetProperty(o => o.UpdatedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    protected override async Task<IReadOnlySet<long>> CheckHeldCoreAsync(
        IReadOnlyList<HeldCharacter> held, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        var ids = held.Select(h => h.CharacterId).ToArray();
        var owners = await db.CharacterOwners.AsNoTracking()
            .Where(o => ids.Contains(o.CharacterId))
            .Select(o => new { o.CharacterId, o.ServerId, o.Version, o.ReleasedAt })
            .ToDictionaryAsync(o => o.CharacterId, cancellationToken);

        var still = held
            .Where(h => owners.TryGetValue(h.CharacterId, out var o)
                && o.ServerId == Options.ServerId && o.Version == h.Version && o.ReleasedAt == null)
            .Select(h => h.CharacterId)
            .ToHashSet();

        // The held rows' updated_at works as a heartbeat (the guest cleanup counts it as activity).
        var stillIds = still.ToArray();
        var now = Now;
        await db.CharacterOwners
            .Where(o => stillIds.Contains(o.CharacterId))
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.UpdatedAt, now), cancellationToken);
        return still;
    }

    public override async Task<string?> LoadNameAsync(long characterId, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        return await db.Characters.AsNoTracking()
            .Where(c => c.Id == characterId && c.DeletedAt == null)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
    }

    protected override bool IsTransient(Exception exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is NpgsqlException { IsTransient: true } or TimeoutException or SocketException
                || e is PostgresException { SqlState: PostgresErrorCodes.CannotConnectNow or PostgresErrorCodes.AdminShutdown })
            {
                return true;
            }
        }

        return false;
    }

    private DateTime Now => Clock.GetUtcNow().UtcDateTime;

    private async Task<IReadOnlySet<long>> SaveStatusesAsync(
        TContext db, IReadOnlyList<StatusSave> saves, CancellationToken cancellationToken)
    {
        await using var command = Command(db, CharacterRepositorySql.SaveStatuses);
        command.Parameters.AddWithValue("characters", saves.Select(s => s.CharacterId).ToArray());
        command.Parameters.AddWithValue("versions", saves.Select(s => s.Version).ToArray());
        command.Parameters.AddWithValue("zones", saves.Select(s => s.Position.Zone).ToArray());
        command.Parameters.AddWithValue("xs", NpgsqlDbType.Array | NpgsqlDbType.Real, saves.Select(s => s.Position.X).ToArray());
        command.Parameters.AddWithValue("ys", NpgsqlDbType.Array | NpgsqlDbType.Real, saves.Select(s => s.Position.Y).ToArray());
        command.Parameters.AddWithValue("zs", NpgsqlDbType.Array | NpgsqlDbType.Real, saves.Select(s => s.Position.Z).ToArray());
        command.Parameters.AddWithValue("facings", NpgsqlDbType.Array | NpgsqlDbType.Real, saves.Select(s => s.Position.Facing).ToArray());
        command.Parameters.AddWithValue("server", Options.ServerId);
        command.Parameters.AddWithValue("now", Now);

        var applied = new HashSet<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetInt64(0));
        }

        return applied;
    }

    // Locks the owner row and says whether this server holds it under the version.
    private async Task<bool> LockHeldAsync(TContext db, long characterId, long version, CancellationToken cancellationToken)
    {
        await using var command = Command(db, CharacterRepositorySql.LockOwner);
        command.Parameters.AddWithValue("character", characterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            && reader.GetInt32(0) == Options.ServerId
            && reader.GetInt64(1) == version
            && reader.GetBoolean(2);
    }

    // A command on the context's connection, in its transaction if it has one.
    private static NpgsqlCommand Command(TContext db, string sql) =>
        new(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
}
