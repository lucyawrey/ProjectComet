using Comet.Data.Characters;
using Comet.Server.Joining;
using Comet.Tickets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comet.Server.Tests;

public sealed class JoinedCharactersTests : IAsyncDisposable
{
    private const int ServerId = 7;
    private static readonly CharacterPosition Here = new(1, 2f, 3f, 4f, 0.5f);
    private static readonly (string Private, string Public) Keys = TicketKeys.Create();

    private readonly InMemoryCharacterDatabase<Character> _db = new(TimeProvider.System);
    private readonly InMemoryCharacterRepository<Character> _repository;
    private readonly TicketIssuer _issuer = new(TicketKeys.PrivateKey(Keys.Private), TimeProvider.System);
    private readonly JoinedCharacters<Client, Character> _joins;
    private readonly Host _host = new();

    public JoinedCharactersTests()
    {
        // A second ago, so this test's tickets count as issued after the server started.
        var checker = new TicketChecker([TicketKeys.PublicKey(Keys.Public)], ServerId, new ShiftedClock(TimeSpan.FromSeconds(-1)));
        _repository = new(_db, new CharacterRepositoryOptions { ServerId = ServerId, SaveInterval = TimeSpan.FromHours(1) });
        _joins = new(checker, _repository, TimeSpan.FromSeconds(10), NullLogger.Instance);
        _db.Add(
            new Character
            {
                Id = 100,
                AccountId = 1,
                Name = "Alice",
            }
        );
        _db.Add(
            new Character
            {
                Id = 200,
                AccountId = 2,
                Name = "Bob",
            }
        );
    }

    public async ValueTask DisposeAsync() => await _repository.DisposeAsync();

    [Fact]
    public async Task AGoodTicketJoinsItsCharacter()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var client = new Client("a");

        Assert.True(_joins.Begin(client, _issuer.Issue(1, 100, ServerId)));
        await UntilAsync(() => _host.Events.Count > 0);

        Assert.Equal(["joined a Alice v1"], _host.Events);
        Assert.True(_joins.TryGet(client, out var character, out var account));
        Assert.Equal((100, 1), (character, account));
        Assert.False(_joins.Begin(client, _issuer.Issue(1, 100, ServerId))); // one join per connection
    }

    [Fact]
    public async Task BadTicketsFail()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var used = _issuer.Issue(1, 100, ServerId);
        _joins.Begin(new Client("first"), used);
        await UntilAsync(() => _host.Events.Count > 0);

        _joins.Begin(new Client("reuse"), used);
        _joins.Begin(new Client("other server"), _issuer.Issue(1, 100, ServerId + 1));
        _joins.Begin(new Client("garbage"), "not a ticket");
        _joins.Begin(new Client("missing"), _issuer.Issue(3, 300, ServerId));
        await UntilAsync(() => _host.Events.Count == 5);

        Assert.Equal(
            [
                "failed garbage BadTicket",
                "failed missing NoCharacter",
                "failed other server WrongServer",
                "failed reuse UsedTicket",
                "joined first Alice v1",
            ],
            _host.Events.Order()
        );
    }

    [Fact]
    public async Task ATicketForAnotherAccountsCharacterFails()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        _joins.Begin(new Client("thief"), _issuer.Issue(2, 100, ServerId)); // account 2, Alice's character
        await UntilAsync(() => _host.Events.Count > 0);

        Assert.Equal(["failed thief BadTicket"], _host.Events);
        await _repository.FlushAsync();
        Assert.False(_db.Owner(100)!.Value.Held);
    }

    [Fact]
    public async Task TheNewerConnectionWinsAndTheOlderOnesSaveGoesFirst()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var laptop = new Client("laptop");
        _joins.Begin(laptop, _issuer.Issue(1, 100, ServerId));
        await UntilAsync(() => _host.Events.Count == 1);

        _host.Positions[laptop] = Here;
        var phone = new Client("phone");
        _joins.Begin(phone, _issuer.Issue(1, 100, ServerId));
        await UntilAsync(() => _host.Events.Count == 3);

        Assert.Equal(["joined laptop Alice v1", "dropped laptop replaced", "joined phone Alice v2 at Zone 1"], _host.Events);
        Assert.False(_joins.TryGet(laptop, out _, out _));
        Assert.True(_joins.TryGet(phone, out _, out _));

        // The laptop's late save is refused, and only the phone stays (the refusal drops nobody, since the laptop is gone).
        _repository.SaveStatus(100, 1, Here with { X = 99 });
        await _repository.FlushAsync();
        _joins.Update(_host);
        Assert.Equal(3, _host.Events.Count);
        Assert.Equal(Here, _db.Position(100));
    }

    [Fact]
    public async Task ARefusedSaveDropsThePlayer()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var client = new Client("a");
        _joins.Begin(client, _issuer.Issue(1, 100, ServerId));
        await UntilAsync(() => _host.Events.Count == 1);

        _db.Kick(100); // what a ban does
        await _repository.FlushAsync();
        await UntilAsync(() => _host.Events.Count == 2);

        Assert.Equal("dropped a refused", _host.Events[^1]);
        Assert.False(_joins.TryGet(client, out _, out _));
    }

    [Fact]
    public async Task LeavingSavesAndReleases()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var client = new Client("a");
        _joins.Begin(client, _issuer.Issue(1, 100, ServerId));
        await UntilAsync(() => _host.Events.Count == 1);

        await _joins.Leave(client, Here);

        Assert.Equal(Here, _db.Position(100));
        Assert.False(_db.Owner(100)!.Value.Held);
        Assert.Equal(0, _joins.Count);
    }

    [Fact]
    public async Task AConnectionThatClosesWhileJoiningHoldsNothing()
    {
        await _repository.StartAsync(TestContext.Current.CancellationToken);
        var client = new Client("a");
        _joins.Begin(client, _issuer.Issue(1, 100, ServerId));
        await _joins.Leave(client, null); // before Update runs the finished join

        await Task.Delay(100, TestContext.Current.CancellationToken);
        _joins.Update(_host);
        await _repository.FlushAsync();

        Assert.Empty(_host.Events);
        var owner = _db.Owner(100);
        Assert.True(owner is null or { Held: false });
    }

    [Fact]
    public async Task AJoinFailsAsBusyWhenTheDatabaseCantAnswer()
    {
        // Never started: the repository's writer isn't running, so the claim can't finish before the deadline.
        var joins = new JoinedCharacters<Client, Character>(
            new TicketChecker([TicketKeys.PublicKey(Keys.Public)], ServerId, new ShiftedClock(TimeSpan.FromSeconds(-1))),
            _repository,
            TimeSpan.FromMilliseconds(100),
            NullLogger.Instance
        );
        joins.Begin(new Client("a"), _issuer.Issue(1, 100, ServerId));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_host.Events.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            joins.Update(_host);
        }

        Assert.Equal(["failed a Busy"], _host.Events);
    }

    private async Task UntilAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            _joins.Update(_host);
        }

        Assert.True(done(), $"Timed out; events: {string.Join(", ", _host.Events)}");
    }

    private sealed record Client(string Name);

    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + shift;
    }

    private sealed class Host : IJoinHost<Client, Character>
    {
        public List<string> Events { get; } = [];
        public Dictionary<Client, CharacterPosition> Positions { get; } = [];

        public void JoinFailed(Client connection, JoinFailure failure) => Events.Add($"failed {connection.Name} {failure}");

        public void Joined(Client connection, CharacterClaim<Character> claim) =>
            Events.Add($"joined {connection.Name} {claim.Character!.Name} v{claim.Version}{(claim.Position is { } p ? $" at Zone {p.Zone}" : "")}");

        public CharacterPosition? Dropped(Client connection, bool replaced)
        {
            Events.Add($"dropped {connection.Name} {(replaced ? "replaced" : "refused")}");
            return Positions.TryGetValue(connection, out var position) ? position : null;
        }
    }
}
