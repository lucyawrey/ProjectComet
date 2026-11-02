using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comet.Data.Characters;

/// <summary>
/// The rows an <see cref="InMemoryCharacterRepository{TCharacter}"/> reads and writes, shareable between several
/// repositories (servers) as one database would be. It also does what the Login server does to the region (adding
/// characters, renaming, deleting, kicking), for tests and for a development server without accounts, which can
/// keep the characters and their spots in a snapshot file (<see cref="Load"/>). Thread-safe.
/// </summary>
public sealed class InMemoryCharacterDatabase<TCharacter>(TimeProvider clock)
    where TCharacter : Character
{
    // The game's own columns: what a look save writes.
    private static readonly PropertyInfo[] GameProperties = typeof(TCharacter)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.DeclaringType != typeof(Character) && p.CanWrite && p.CanRead)
        .ToArray();

    private readonly Lock _lock = new();
    private readonly Dictionary<long, TCharacter> _characters = [];
    private readonly Dictionary<long, CharacterOwner> _owners = [];
    private readonly Dictionary<long, CharacterStatus> _statuses = [];
    private string? _snapshot; // the file every change is written to, once loaded from one

    public TimeProvider Clock => clock;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public void Add(TCharacter character)
    {
        lock (_lock)
        {
            AddLocked(character);
        }
    }

    /// <summary>
    /// The live character with this name, ignoring case (as the name column does), or a new one from
    /// <paramref name="make"/>, given the next free ID. Returns its ID, and whether it was made.
    /// </summary>
    public (long Id, bool Made) FindOrAdd(string name, Func<long, TCharacter> make)
    {
        lock (_lock)
        {
            var found = _characters.Values.FirstOrDefault(c => !c.IsDeleted && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return (found.Id, false);
            }

            var id = _characters.Count == 0 ? 1 : _characters.Keys.Max() + 1;
            AddLocked(make(id));
            return (id, true);
        }
    }

    /// <summary>
    /// Reads the characters and their spots from a snapshot file, and from then on writes it after every change. A
    /// missing file starts empty; one that won't read (damaged, or from an older character class) is renamed to
    /// <c>.bad</c> and the database starts empty, returning why. Owners aren't kept, so no character loads held.
    /// </summary>
    public string? Load(string path)
    {
        lock (_lock)
        {
            _snapshot = path;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path))
                    ?? throw new JsonException("The file is empty.");
                _characters.Clear();
                _statuses.Clear();
                foreach (var character in snapshot.Characters)
                {
                    _characters.Add(character.Id, character);
                }

                foreach (var status in snapshot.Statuses.Where(s => _characters.ContainsKey(s.CharacterId)))
                {
                    _statuses.Add(status.CharacterId, status);
                }

                return null;
            }
            catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException)
            {
                _characters.Clear();
                _statuses.Clear();
                File.Move(path, path + ".bad", overwrite: true);
                return e.Message;
            }
        }
    }

    /// <summary>A copy of the character, deleted or not, or null.</summary>
    public TCharacter? Character(long characterId)
    {
        lock (_lock)
        {
            return _characters.TryGetValue(characterId, out var character) ? (TCharacter)character.Copy() : null;
        }
    }

    public CharacterPosition? Position(long characterId)
    {
        lock (_lock)
        {
            return _statuses.TryGetValue(characterId, out var s) ? new(s.Zone, s.X, s.Y, s.Z, s.Facing) : null;
        }
    }

    /// <summary>The owner row's server, version and whether a server holds it now, or null before the first claim.</summary>
    public (int ServerId, long Version, bool Held, DateTime UpdatedAt)? Owner(long characterId)
    {
        lock (_lock)
        {
            return _owners.TryGetValue(characterId, out var o) ? (o.ServerId, o.Version, o.IsHeld, o.UpdatedAt) : null;
        }
    }

    /// <summary>Sets the saved position, as an earlier session's save would have.</summary>
    public void SetPosition(long characterId, CharacterPosition position)
    {
        lock (_lock)
        {
            WriteStatusLocked(characterId, position);
            WriteSnapshotLocked();
        }
    }

    public void Rename(long characterId, string name)
    {
        lock (_lock)
        {
            var character = _characters[characterId];
            character.Name = name;
            character.UpdatedAt = Now;
            WriteSnapshotLocked();
        }
    }

    /// <summary>Marks the character deleted and kicks it, as deleting its account does.</summary>
    public void Delete(long characterId)
    {
        lock (_lock)
        {
            _characters[characterId].DeletedAt = Now;
            BumpLocked(characterId);
            WriteSnapshotLocked();
        }
    }

    /// <summary>Adds one to the owner version, as a ban or deletion does, so the holding server drops the player.</summary>
    public void Kick(long characterId)
    {
        lock (_lock)
        {
            BumpLocked(characterId);
        }
    }

    internal void ReleaseServer(int serverId)
    {
        lock (_lock)
        {
            foreach (var owner in _owners.Values.Where(o => o.ServerId == serverId && o.IsHeld))
            {
                owner.ReleasedAt = owner.UpdatedAt = Now;
            }
        }
    }

    internal CharacterClaim<TCharacter> Claim(long characterId, int serverId)
    {
        lock (_lock)
        {
            if (!_characters.TryGetValue(characterId, out var character) || character.IsDeleted)
            {
                return new(ClaimOutcome.NotFound);
            }

            if (_owners.TryGetValue(characterId, out var owner))
            {
                owner.ServerId = serverId;
                owner.Version++;
                owner.UpdatedAt = Now;
                owner.ReleasedAt = null;
            }
            else
            {
                owner = new() { CharacterId = characterId, ServerId = serverId, Version = 1, CreatedAt = Now, UpdatedAt = Now };
                _owners.Add(characterId, owner);
            }

            CharacterPosition? position = _statuses.TryGetValue(characterId, out var s)
                ? new(s.Zone, s.X, s.Y, s.Z, s.Facing)
                : null;
            return new(ClaimOutcome.Claimed, (TCharacter)character.Copy(), position, owner.Version);
        }
    }

    internal HashSet<long> SaveStatuses(IReadOnlyList<StatusSave> saves, int serverId)
    {
        lock (_lock)
        {
            var applied = new HashSet<long>();
            foreach (var save in saves.Where(save => HoldsLocked(save.CharacterId, save.Version, serverId)))
            {
                WriteStatusLocked(save.CharacterId, save.Position);
                applied.Add(save.CharacterId);
            }

            if (applied.Count > 0)
            {
                WriteSnapshotLocked();
            }

            return applied;
        }
    }

    internal bool SaveCharacter(TCharacter character, long version, int serverId)
    {
        lock (_lock)
        {
            if (!HoldsLocked(character.Id, version, serverId))
            {
                return false;
            }

            var stored = _characters[character.Id];
            foreach (var property in GameProperties)
            {
                property.SetValue(stored, property.GetValue(character));
            }

            stored.UpdatedAt = Now;
            WriteSnapshotLocked();
            return true;
        }
    }

    internal void Leave(long characterId, long version, CharacterPosition? position, int serverId)
    {
        lock (_lock)
        {
            if (!HoldsLocked(characterId, version, serverId))
            {
                return;
            }

            if (position is { } final)
            {
                WriteStatusLocked(characterId, final);
                WriteSnapshotLocked();
            }

            var owner = _owners[characterId];
            owner.ReleasedAt = owner.UpdatedAt = Now;
        }
    }

    internal HashSet<long> CheckHeld(IReadOnlyList<HeldCharacter> held, int serverId)
    {
        lock (_lock)
        {
            var still = held.Where(h => HoldsLocked(h.CharacterId, h.Version, serverId)).Select(h => h.CharacterId).ToHashSet();
            foreach (var id in still)
            {
                _owners[id].UpdatedAt = Now;
            }

            return still;
        }
    }

    internal string? LoadName(long characterId)
    {
        lock (_lock)
        {
            return _characters.TryGetValue(characterId, out var c) && !c.IsDeleted ? c.Name : null;
        }
    }

    private void AddLocked(TCharacter character)
    {
        var copy = (TCharacter)character.Copy();
        copy.CreatedAt = copy.UpdatedAt = Now;
        _characters.Add(copy.Id, copy);
        WriteSnapshotLocked();
    }

    // Under the lock, so one write goes at a time and each has the latest rows; written beside the file and renamed
    // over it, so a crash mid-write leaves the last good one.
    private void WriteSnapshotLocked()
    {
        if (_snapshot == null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_snapshot))!);
        var next = _snapshot + ".new";
        File.WriteAllText(next, JsonSerializer.Serialize(new Snapshot([.. _characters.Values], [.. _statuses.Values])));
        File.Move(next, _snapshot, overwrite: true);
    }

    private sealed record Snapshot(List<TCharacter> Characters, List<CharacterStatus> Statuses);

    private bool HoldsLocked(long characterId, long version, int serverId) =>
        _owners.TryGetValue(characterId, out var owner)
        && owner.ServerId == serverId && owner.Version == version && owner.IsHeld;

    private void BumpLocked(long characterId)
    {
        if (_owners.TryGetValue(characterId, out var owner))
        {
            owner.Version++;
            owner.UpdatedAt = Now;
        }
    }

    private void WriteStatusLocked(long characterId, CharacterPosition position)
    {
        if (!_statuses.TryGetValue(characterId, out var status))
        {
            status = new() { CharacterId = characterId, Zone = 0, X = 0, Y = 0, Z = 0, Facing = 0, CreatedAt = Now };
            _statuses.Add(characterId, status);
        }

        (status.Zone, status.X, status.Y, status.Z, status.Facing) = (position.Zone, position.X, position.Y, position.Z, position.Facing);
        status.UpdatedAt = Now;
    }
}

/// <summary>
/// The character repository in memory, for game-server tests and development servers without accounts: the same contract as the PostgreSQL
/// <see cref="CharacterRepository{TCharacter, TContext}"/> (claims, versions, refused saves, releases and the save
/// queue), checked by the same contract tests.
/// </summary>
public sealed class InMemoryCharacterRepository<TCharacter>(
    InMemoryCharacterDatabase<TCharacter> database, CharacterRepositoryOptions options, ILogger? logger = null)
    : QueuedCharacterRepository<TCharacter>(options, database.Clock, logger ?? NullLogger.Instance)
    where TCharacter : Character
{
    public InMemoryCharacterDatabase<TCharacter> Database => database;

    protected override Task PrepareAsync(CancellationToken cancellationToken)
    {
        database.ReleaseServer(Options.ServerId);
        return Task.CompletedTask;
    }

    protected override Task<CharacterClaim<TCharacter>> ClaimCoreAsync(long characterId, CancellationToken cancellationToken) =>
        Task.FromResult(database.Claim(characterId, Options.ServerId));

    protected override Task<IReadOnlySet<long>> SaveStatusesCoreAsync(IReadOnlyList<StatusSave> saves, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(database.SaveStatuses(saves, Options.ServerId));

    protected override Task<bool> SaveCharacterCoreAsync(TCharacter character, long version, CancellationToken cancellationToken) =>
        Task.FromResult(database.SaveCharacter(character, version, Options.ServerId));

    protected override Task LeaveCoreAsync(long characterId, long version, CharacterPosition? position, CancellationToken cancellationToken)
    {
        database.Leave(characterId, version, position, Options.ServerId);
        return Task.CompletedTask;
    }

    protected override Task<IReadOnlySet<long>> CheckHeldCoreAsync(IReadOnlyList<HeldCharacter> held, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(database.CheckHeld(held, Options.ServerId));

    public override Task<string?> LoadNameAsync(long characterId, CancellationToken cancellationToken) =>
        Task.FromResult(database.LoadName(characterId));
}
