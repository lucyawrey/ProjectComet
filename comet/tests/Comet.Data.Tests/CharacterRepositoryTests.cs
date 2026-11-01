using System.Collections.Concurrent;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comet.Data.Tests;

/// <summary>
/// The character repository's contract, run against both the in-memory and the PostgreSQL repository, so the
/// in-memory one the game tests use behaves like the real one.
/// </summary>
public abstract class CharacterRepositoryContract : IAsyncLifetime
{
    private const int ServerA = 1;
    private const int ServerB = 2;
    private static readonly CharacterPosition Somewhere = new(3, 1.5f, 2.25f, -7f, 0.5f);
    private static readonly CharacterPosition Elsewhere = new(4, -10f, 0f, 12.5f, 3f);

    private readonly List<ICharacterRepository<TestCharacter>> _repositories = [];
    private readonly ConcurrentQueue<(int Server, SaveRefused Refused)> _refused = new();
    private long _nextId = 1000;

    protected abstract Task<IBackend> CreateBackendAsync();

    private IBackend Backend { get; set; } = null!;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => Backend = await CreateBackendAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var repository in _repositories)
        {
            await repository.DisposeAsync();
        }
    }

    [Fact]
    public async Task AClaimLoadsTheCharacterWithNoPositionBeforeItsFirstSave()
    {
        var id = await AddAsync("Alice", look: 7);
        var repository = await StartAsync(ServerA);

        var claim = await repository.ClaimAsync(id, Cancel);

        Assert.Equal(ClaimOutcome.Claimed, claim.Outcome);
        Assert.Equal("Alice", claim.Character!.Name);
        Assert.Equal(7, claim.Character.Look);
        Assert.Null(claim.Position);
        Assert.Equal(1, claim.Version);
        Assert.Equal((ServerA, 1L, true), await Backend.OwnerAsync(id));
    }

    [Fact]
    public async Task MissingAndDeletedCharactersAreNotFound()
    {
        var id = await AddAsync("Bob");
        await Backend.DeleteAsync(id);
        var repository = await StartAsync(ServerA);

        Assert.Equal(ClaimOutcome.NotFound, (await repository.ClaimAsync(id, Cancel)).Outcome);
        Assert.Equal(ClaimOutcome.NotFound, (await repository.ClaimAsync(424242, Cancel)).Outcome);
    }

    [Fact]
    public async Task StatusSavesWaitForTheIntervalAndTheLatestWins()
    {
        var id = await AddAsync("Cleo");
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        repository.SaveStatus(id, claim.Version, Elsewhere);
        repository.SaveStatus(id, claim.Version, Somewhere);
        Assert.Null(await Backend.PositionAsync(id));

        await repository.FlushAsync();
        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
        Assert.Empty(_refused);
    }

    [Fact]
    public async Task ABatchWritesEveryCharacter()
    {
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await AddAsync($"Many{i}"));
        }

        var repository = await StartAsync(ServerA);
        foreach (var id in ids)
        {
            var claim = await repository.ClaimAsync(id, Cancel);
            repository.SaveStatus(id, claim.Version, Somewhere with { X = id });
        }

        await repository.FlushAsync();
        foreach (var id in ids)
        {
            Assert.Equal(Somewhere with { X = id }, await Backend.PositionAsync(id));
        }
    }

    [Fact]
    public async Task LeavingWritesTheFinalSaveAndReleases()
    {
        var id = await AddAsync("Dana");
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        repository.SaveStatus(id, claim.Version, Elsewhere);
        await repository.LeaveAsync(id, claim.Version, Somewhere);

        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
        Assert.Equal((ServerA, 1L, false), await Backend.OwnerAsync(id));

        // The queued save it replaced isn't written later.
        await repository.FlushAsync();
        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
        Assert.Empty(_refused);
    }

    [Fact]
    public async Task LeavingWithNoPositionKeepsTheQueuedOne()
    {
        var id = await AddAsync("Edda");
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        repository.SaveStatus(id, claim.Version, Elsewhere);
        await repository.LeaveAsync(id, claim.Version, null);

        Assert.Equal(Elsewhere, await Backend.PositionAsync(id));
    }

    [Fact]
    public async Task RejoiningWithinAnIntervalLoadsTheLatestPosition()
    {
        var id = await AddAsync("Finn");
        var repository = await StartAsync(ServerA);
        var first = await repository.ClaimAsync(id, Cancel);
        repository.SaveStatus(id, first.Version, Elsewhere);

        // The newer connection: the older one leaves first (not awaited, as the game does), then the claim.
        _ = repository.LeaveAsync(id, first.Version, Somewhere);
        var second = await repository.ClaimAsync(id, Cancel);

        Assert.Equal(2, second.Version);
        Assert.Equal(Somewhere, second.Position);

        // The older connection's late save is refused; the newer one keeps playing.
        repository.SaveStatus(id, first.Version, Elsewhere);
        await repository.FlushAsync();
        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
        Assert.Equal([(ServerA, new SaveRefused(id, first.Version))], _refused);

        repository.SaveStatus(id, second.Version, Elsewhere);
        await repository.FlushAsync();
        Assert.Equal(Elsewhere, await Backend.PositionAsync(id));
        Assert.Single(_refused);
    }

    [Fact]
    public async Task AClaimWaitsForItsCharactersQueuedStatus()
    {
        var id = await AddAsync("Gus");
        var repository = await StartAsync(ServerA);
        var first = await repository.ClaimAsync(id, Cancel);

        // The older connection never left (it's still being dropped): its queued save still lands before the claim.
        repository.SaveStatus(id, first.Version, Somewhere);
        var second = await repository.ClaimAsync(id, Cancel);

        Assert.Equal(Somewhere, second.Position);
    }

    [Fact]
    public async Task AnotherServersClaimRefusesTheOldServersSaves()
    {
        var id = await AddAsync("Hana", look: 1);
        var a = await StartAsync(ServerA);
        var b = await StartAsync(ServerB);
        var old = await a.ClaimAsync(id, Cancel);
        var current = await b.ClaimAsync(id, Cancel);
        Assert.Equal(2, current.Version);

        a.SaveStatus(id, old.Version, Elsewhere);
        a.SaveCharacter(Look(old.Character!, 5), old.Version);
        await a.FlushAsync();

        Assert.Null(await Backend.PositionAsync(id));
        Assert.Equal(1, (await Backend.CharacterAsync(id))!.Look);
        Assert.All(_refused, r => Assert.Equal((ServerA, new SaveRefused(id, old.Version)), r));
        Assert.NotEmpty(_refused);

        b.SaveStatus(id, current.Version, Somewhere);
        b.SaveCharacter(Look(current.Character!, 9), current.Version);
        await b.FlushAsync();
        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
        Assert.Equal(9, (await Backend.CharacterAsync(id))!.Look);
        Assert.DoesNotContain(_refused, r => r.Server == ServerB);
    }

    [Fact]
    public async Task ALookSaveDoesntUndoARename()
    {
        var id = await AddAsync("Iris", look: 1);
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        await Backend.RenameAsync(id, "Isolde");
        repository.SaveCharacter(Look(claim.Character!, 4), claim.Version); // still carries "Iris"
        await repository.FlushAsync();

        var saved = await Backend.CharacterAsync(id);
        Assert.Equal("Isolde", saved!.Name);
        Assert.Equal(4, saved.Look);
        Assert.Equal("Isolde", await repository.LoadNameAsync(id, Cancel));
    }

    [Fact]
    public async Task ALookSaveDoesntUndoADeletion()
    {
        var id = await AddAsync("Jade", look: 1);
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        await Backend.DeleteAsync(id);
        repository.SaveCharacter(Look(claim.Character!, 4), claim.Version);
        await repository.FlushAsync();

        var saved = await Backend.CharacterAsync(id);
        Assert.NotNull(saved!.DeletedAt);
        Assert.Equal(1, saved.Look);
        Assert.Contains((ServerA, new SaveRefused(id, claim.Version)), _refused);
        Assert.Null(await repository.LoadNameAsync(id, Cancel));
    }

    [Fact]
    public async Task AKickDropsAnIdlePlayerAtTheNextInterval()
    {
        var id = await AddAsync("Kai");
        var repository = await StartAsync(ServerA, TimeSpan.FromMilliseconds(100));
        var claim = await repository.ClaimAsync(id, Cancel);

        await Backend.KickAsync(id);

        // No saves at all: the interval's version check finds it.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_refused.IsEmpty && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, Cancel);
        }

        Assert.Equal([(ServerA, new SaveRefused(id, claim.Version))], _refused);
    }

    [Fact]
    public async Task TheVersionCheckStampsHeldCharactersAsActive()
    {
        var id = await AddAsync("Lux");
        var repository = await StartAsync(ServerA);
        await repository.ClaimAsync(id, Cancel);
        var before = await Backend.OwnerUpdatedAtAsync(id);

        await Task.Delay(50, Cancel);
        await repository.FlushAsync();

        Assert.True(await Backend.OwnerUpdatedAtAsync(id) > before);
        Assert.Empty(_refused);
    }

    [Fact]
    public async Task StartingReleasesWhatTheServerStillHeld()
    {
        var id = await AddAsync("Milo");
        var crashed = await StartAsync(ServerA);
        await crashed.ClaimAsync(id, Cancel); // and never leaves

        await StartAsync(ServerA);

        Assert.Equal((ServerA, 1L, false), await Backend.OwnerAsync(id));
    }

    [Fact]
    public async Task ACancelledClaimIsUnavailableAndHoldsNothing()
    {
        var id = await AddAsync("Nia");
        var repository = await StartAsync(ServerA);

        var claim = await repository.ClaimAsync(id, new CancellationToken(canceled: true));

        Assert.Equal(ClaimOutcome.Unavailable, claim.Outcome);
        await repository.FlushAsync();
        var owner = await Backend.OwnerAsync(id);
        Assert.True(owner is null or { Held: false });
    }

    [Fact]
    public async Task StoppingWritesWhatsQueued()
    {
        var id = await AddAsync("Odo");
        var repository = await StartAsync(ServerA);
        var claim = await repository.ClaimAsync(id, Cancel);

        repository.SaveStatus(id, claim.Version, Somewhere);
        await repository.StopAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(Somewhere, await Backend.PositionAsync(id));
    }

    private static TestCharacter Look(TestCharacter character, int look)
    {
        character.Look = look;
        return character;
    }

    private async Task<long> AddAsync(string name, int look = 0)
    {
        var id = Interlocked.Increment(ref _nextId);
        await Backend.AddAsync(new TestCharacter { Id = id, AccountId = id + 1_000_000, Name = name, Look = look });
        return id;
    }

    private async Task<ICharacterRepository<TestCharacter>> StartAsync(int server, TimeSpan? interval = null)
    {
        // A long interval by default, so tests write with FlushAsync, not the timer.
        var options = new CharacterRepositoryOptions { ServerId = server, SaveInterval = interval ?? TimeSpan.FromHours(1) };
        var repository = Backend.CreateRepository(options);
        repository.SaveRefused += refused => _refused.Enqueue((server, refused));
        await repository.StartAsync(Cancel);
        _repositories.Add(repository);
        return repository;
    }

    /// <summary>One database the repositories share, and what the Login server does to it.</summary>
    protected interface IBackend
    {
        ICharacterRepository<TestCharacter> CreateRepository(CharacterRepositoryOptions options);
        Task AddAsync(TestCharacter character);
        Task RenameAsync(long id, string name);
        Task DeleteAsync(long id);
        Task KickAsync(long id);
        Task<TestCharacter?> CharacterAsync(long id);
        Task<CharacterPosition?> PositionAsync(long id);
        Task<(int ServerId, long Version, bool Held)?> OwnerAsync(long id);
        Task<DateTime> OwnerUpdatedAtAsync(long id);
    }
}

public sealed class InMemoryCharacterRepositoryTests : CharacterRepositoryContract
{
    protected override Task<IBackend> CreateBackendAsync() => Task.FromResult<IBackend>(new Memory());

    private sealed class Memory : IBackend
    {
        private readonly InMemoryCharacterDatabase<TestCharacter> _db = new(TimeProvider.System);

        public ICharacterRepository<TestCharacter> CreateRepository(CharacterRepositoryOptions options) => new InMemoryCharacterRepository<TestCharacter>(_db, options);
        public Task AddAsync(TestCharacter character) => Done(() => _db.Add(character));
        public Task RenameAsync(long id, string name) => Done(() => _db.Rename(id, name));
        public Task DeleteAsync(long id) => Done(() => _db.Delete(id));
        public Task KickAsync(long id) => Done(() => _db.Kick(id));
        public Task<TestCharacter?> CharacterAsync(long id) => Task.FromResult(_db.Character(id));
        public Task<CharacterPosition?> PositionAsync(long id) => Task.FromResult(_db.Position(id));

        public Task<(int ServerId, long Version, bool Held)?> OwnerAsync(long id) =>
            Task.FromResult(_db.Owner(id) is { } o ? (o.ServerId, o.Version, o.Held) : ((int, long, bool)?)null);

        public Task<DateTime> OwnerUpdatedAtAsync(long id) => Task.FromResult(_db.Owner(id)!.Value.UpdatedAt);

        private static Task Done(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}

public sealed class CharacterRepositoryTests(PostgresContainer postgres) : CharacterRepositoryContract
{
    protected override async Task<IBackend> CreateBackendAsync()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var backend = new Postgres(connection);
        await using var db = backend.Context();
        await db.Database.EnsureCreatedAsync();
        return backend;
    }

    private sealed class Postgres(string connection) : IBackend
    {
        public TestRegionContext Context()
        {
            var options = new DbContextOptionsBuilder<TestRegionContext>();
            options.UseCometRegion(connection, TimeProvider.System);
            return new(options.Options);
        }

        public ICharacterRepository<TestCharacter> CreateRepository(CharacterRepositoryOptions options) =>
            new CharacterRepository<TestCharacter, TestRegionContext>(Context, options, TimeProvider.System, NullLogger.Instance);

        public async Task AddAsync(TestCharacter character)
        {
            await using var db = Context();
            db.Characters.Add(character);
            await db.SaveChangesAsync();
        }

        public async Task RenameAsync(long id, string name)
        {
            await using var db = Context();
            await db.Characters.Where(c => c.Id == id).ExecuteUpdateAsync(set => set.SetProperty(c => c.Name, name));
        }

        public async Task DeleteAsync(long id)
        {
            await using var db = Context();
            await db.Characters.Where(c => c.Id == id).ExecuteUpdateAsync(set => set.SetProperty(c => c.DeletedAt, DateTime.UtcNow));
            await KickAsync(id);
        }

        public async Task KickAsync(long id)
        {
            await using var db = Context();
            await db.CharacterOwners.Where(o => o.CharacterId == id).ExecuteUpdateAsync(set => set.SetProperty(o => o.Version, o => o.Version + 1));
        }

        public async Task<TestCharacter?> CharacterAsync(long id)
        {
            await using var db = Context();
            return await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
        }

        public async Task<CharacterPosition?> PositionAsync(long id)
        {
            await using var db = Context();
            var s = await db.CharacterStatuses.AsNoTracking().SingleOrDefaultAsync(s => s.CharacterId == id);
            return s == null ? null : new(s.Zone, s.X, s.Y, s.Z, s.Facing);
        }

        public async Task<(int ServerId, long Version, bool Held)?> OwnerAsync(long id)
        {
            await using var db = Context();
            var o = await db.CharacterOwners.AsNoTracking().SingleOrDefaultAsync(o => o.CharacterId == id);
            return o == null ? null : (o.ServerId, o.Version, o.IsHeld);
        }

        public async Task<DateTime> OwnerUpdatedAtAsync(long id)
        {
            await using var db = Context();
            return (await db.CharacterOwners.AsNoTracking().SingleAsync(o => o.CharacterId == id)).UpdatedAt;
        }
    }
}
