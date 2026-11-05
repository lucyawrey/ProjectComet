using System.Net.WebSockets;
using System.Numerics;
using Comet.Content;
using Comet.Data.Characters;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Tickets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShapeLand.Bots;
using ShapeLand.ContentBuild;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer.Tests;

/// <summary>
/// Runs the real server in-process on a free port, with bots connecting over WebSockets. Players start in the tests'
/// own zone (<see cref="TestsZone"/>, at <see cref="StartSpawn"/>), so editing ShapeLand's zones can't break the tests.
/// Characters live in an in-memory repository, and joins carry real tickets signed with a test key, as the Login
/// server would issue them.
/// </summary>
public sealed class EndToEndTests : IAsyncLifetime
{
    private const string TestsZone = "zone.tests";
    private const string StartSpawn = "spawn.tests.start";
    private const int ServerId = 1;
    private static readonly (string Private, string Public) Keys = TicketKeys.Create();
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMilliseconds(200);

    private readonly InMemoryCharacterDatabase<ShapeLandCharacter> _db = new(TimeProvider.System);
    private readonly TicketIssuer _issuer = new(TicketKeys.PrivateKey(Keys.Private), TimeProvider.System);
    private readonly Dictionary<string, long> _characters = new(StringComparer.OrdinalIgnoreCase);
    private long _nextId = 1000;

    private readonly string _contentFile = Path.Combine(Path.GetTempPath(), $"shapeland-content-{Guid.NewGuid():N}.bin");
    private ShapeLandContent _content = null!;
    private WebApplication _server = null!;
    private Uri _url = null!;

    public async ValueTask InitializeAsync()
    {
        var (content, errors) = ShapeLandBuild.Build(Path.Combine(RepoRoot(), "shapeland", "content"), saveRegistry: false);
        Assert.Empty(errors);
        _content = content!;
        await using (var file = File.Create(_contentFile))
        {
            _content.Save(file);
        }

        (_server, _url) = await StartServer(StartSpawn);
    }

    // A server on a free port starting every player at startSpawn (null for none: each where they left, or at their
    // shape's spawn), with its own node number.
    private async Task<(WebApplication Server, Uri Url)> StartServer(string? startSpawn, int serverId = ServerId)
    {
        var server = ShapeLandServer.Build(
            [],
            builder =>
            {
                builder.Configuration["ShapeLand:ContentFile"] = _contentFile;
                builder.Configuration["ShapeLand:StartSpawn"] = startSpawn;
                builder.Configuration["Urls"] = "http://127.0.0.1:0";
                builder.Services.AddSingleton<ICharacterRepository<ShapeLandCharacter>>(
                    new InMemoryCharacterRepository<ShapeLandCharacter>(
                        _db,
                        new CharacterRepositoryOptions { ServerId = serverId, SaveInterval = SaveInterval }
                    )
                );
                builder.Services.AddSingleton(new TicketChecker([TicketKeys.PublicKey(Keys.Public)], serverId, TimeProvider.System));
            }
        );
        await server.StartAsync(TestContext.Current.CancellationToken);
        return (server, new Uri(server.Urls.First().Replace("http://", "ws://") + "/ws"));
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        await _server.DisposeAsync();
        File.Delete(_contentFile);
    }

    private Bot NewBot(string name, int seed, BotSettings settings) =>
        new(name, seed, settings, _content, new ZoneWorlds(_content), new TestAccount(() => new BotJoin(Ticket(name), _url, name)));

    // A bot's account: a ticket for its character, made on first use.
    private sealed class TestAccount(Func<BotJoin> ticket) : IBotAccount
    {
        public Task<(BotJoin? Join, string? Failure)> JoinAsync(CancellationToken cancellationToken) =>
            Task.FromResult<(BotJoin?, string?)>((ticket(), null));
    }

    // The character called name (made on first use, with the first shape and colors unless given), as the Login server
    // would have made it.
    private long Character(string name, int shape = 0, int color = 0)
    {
        if (!_characters.TryGetValue(name, out var id))
        {
            id = Interlocked.Increment(ref _nextId);
            _db.Add(
                new ShapeLandCharacter
                {
                    Id = id,
                    AccountId = id + 1_000_000,
                    Name = name,
                    Shape = _content.Shapes.All[shape].Number,
                    Color = _content.Colors.All[color].Number,
                    EyeColor = _content.Colors.All[color].Number,
                }
            );
            _characters[name] = id;
        }

        return id;
    }

    // A join ticket for name's character, for the server serverId.
    private string Ticket(string name, int serverId = ServerId)
    {
        var id = Character(name);
        return _issuer.Issue(id + 1_000_000, id, serverId);
    }

    private JoinRequest JoinAs(string name, int serverId = ServerId) =>
        new() { Ticket = Ticket(name, serverId), ContentFingerprint = _content.Fingerprint };

    [Fact]
    public async Task PlayersSeeEachOtherChatAndCheatersGetSnappedBack()
    {
        var honest = Enumerable.Range(1, 3).Select(i => NewBot($"Honest{i}", i, new BotSettings { ChatEverySeconds = 1 })).ToList();
        var cheater = NewBot("Cheater", 99, new BotSettings { ChatEverySeconds = 0, SpeedCheat = 2.5f });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(4));

        await Task.WhenAll(honest.Append(cheater).Select(bot => bot.RunAsync(stop.Token)));

        Assert.All(
            honest.Append(cheater),
            bot =>
            {
                Assert.False(bot.Failed, $"{bot.Name} failed");
                Assert.True(bot.Joined, $"{bot.Name} didn't join");
                Assert.Equal(3, bot.SeenPlayers.Count);
                Assert.True(bot.StatesReceived > 30, $"{bot.Name} got only {bot.StatesReceived} position updates");
            }
        );
        Assert.All(
            honest,
            bot =>
            {
                Assert.Equal(0, bot.SnapBacks);
                Assert.True(bot.ChatsReceived >= honest.Sum(b => b.ChatsSent) - 1, $"{bot.Name} heard {bot.ChatsReceived} chat lines");
            }
        );
        Assert.True(cheater.SnapBacks > 0, "the cheater was never snapped back");
    }

    [Fact]
    public async Task TheNewerConnectionWinsAndTheOlderIsToldWhy()
    {
        using var laptop = new RawClient();
        using var phone = new RawClient();
        await JoinAsync(laptop, "Ada");

        await phone.ConnectAsync(_url);
        await phone.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));

        Assert.Equal(DropReason.Replaced, (await laptop.NextAsync<Dropped>(ShapeLandMessageIds.Dropped)).Reason);
        Assert.Equal("Ada", (await phone.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn)).Name);
        Assert.True(await laptop.ClosedAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task BadTicketsAreRefused()
    {
        using var client = new RawClient();
        await client.ConnectAsync(_url);
        await client.SendAsync(
            ShapeLandMessageIds.JoinRequest,
            new JoinRequest { Ticket = "not a ticket", ContentFingerprint = _content.Fingerprint }
        );
        Assert.Equal(JoinRejection.BadTicket, (await client.NextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected)).Reason);

        using var other = new RawClient();
        await other.ConnectAsync(_url);
        await other.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada", serverId: ServerId + 1));
        Assert.Equal(JoinRejection.WrongServer, (await other.NextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected)).Reason);

        // A client with other content is turned away before its ticket is even checked (and used up).
        using var stale = new RawClient();
        await stale.ConnectAsync(_url);
        var join = JoinAs("Bea");
        await stale.SendAsync(ShapeLandMessageIds.JoinRequest, join with { ContentFingerprint = _content.Fingerprint + 1 });
        Assert.Equal(JoinRejection.DifferentContent, (await stale.NextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected)).Reason);
    }

    [Fact]
    public async Task AJoinedPlayerCanChangeTheirLookWhereTheyStandAndItsSaved()
    {
        using var ada = new RawClient();
        using var bea = new RawClient();
        var at = await JoinAsync(ada, "Ada");
        await JoinAsync(bea, "Bea");
        var adaId = (await bea.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn)).EntityId;

        await ada.SendAsync(
            ShapeLandMessageIds.LookChange,
            new LookChange
            {
                Shape = _content.Shapes.All[0].Number,
                Color = 999,
                EyeColor = _content.Colors.All[0].Number,
            }
        );
        Assert.Equal(LookRejection.UnavailableColor, (await ada.NextAsync<LookRejected>(ShapeLandMessageIds.LookRejected)).Reason);
        await ada.SendAsync(
            ShapeLandMessageIds.LookChange,
            new LookChange
            {
                Shape = 999,
                Color = _content.Colors.All[0].Number,
                EyeColor = _content.Colors.All[0].Number,
            }
        );
        Assert.Equal(LookRejection.UnknownShape, (await ada.NextAsync<LookRejected>(ShapeLandMessageIds.LookRejected)).Reason);

        var change = new LookChange
        {
            Shape = _content.Shapes.All[1].Number,
            Color = _content.Colors.All[2].Number,
            EyeColor = _content.Colors.All[3].Number,
        };
        await ada.SendAsync(ShapeLandMessageIds.LookChange, change);
        Assert.Equal(CorrectionReason.SnapBack, (await ada.NextAsync<PositionCorrection>(MessageIds.PositionCorrection)).Reason);
        foreach (var client in new[] { ada, bea })
        {
            PlayerSpawn spawn;
            do
            {
                spawn = await client.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
            } while (spawn.EntityId != adaId || spawn.Shape != change.Shape); // Ada first sees Bea arrive

            Assert.Equal((change.Color, change.EyeColor), (spawn.Color, spawn.EyeColor));
            Assert.Equal(at, new Vector3(spawn.X, spawn.Y, spawn.Z));
        }

        var saved = _db.Character(Character("Ada"))!;
        Assert.Equal((change.Shape, change.Color, change.EyeColor), (saved.Shape, saved.Color, saved.EyeColor));
    }

    [Fact]
    public async Task ARenameIsShownToEveryone()
    {
        using var ada = new RawClient();
        using var bea = new RawClient();
        await JoinAsync(ada, "Ada");
        await JoinAsync(bea, "Bea");
        var adaId = (await bea.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn)).EntityId;

        _db.Rename(Character("Ada"), "Adelaide"); // what the Login server's rename does
        await ada.SendAsync(ShapeLandMessageIds.NameChanged, new NameChanged());

        PlayerSpawn spawn;
        do
        {
            spawn = await bea.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
        } while (spawn.EntityId != adaId);

        Assert.Equal("Adelaide", spawn.Name);
    }

    [Fact]
    public async Task AKickedPlayerIsDroppedEvenStandingStill()
    {
        using var ada = new RawClient();
        await JoinAsync(ada, "Ada");

        _db.Kick(Character("Ada")); // what a ban or deletion does

        Assert.Equal(DropReason.Removed, (await ada.NextAsync<Dropped>(ShapeLandMessageIds.Dropped)).Reason);
        Assert.True(await ada.ClosedAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ReturningPlayersComeBackWhereTheyLeft()
    {
        await WithServer(
            null,
            async url =>
            {
                Vector3 left;
                using (var ada = new RawClient())
                {
                    await ada.ConnectAsync(url);
                    await ada.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));
                    var joined = await ada.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
                    var (zone, _) = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]);
                    var start = new Vector3(joined.X, joined.Y, joined.Z);
                    left = await ada.WalkAsync(ShapeLandWorld.Create(_content, zone), start, start + new Vector3(3, 0, 0), sequence: 0);
                }

                // The final save goes out as the connection closes.
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (_db.Owner(Character("Ada")) is { Held: true } && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }

                using var again = new RawClient();
                await again.ConnectAsync(url);
                await again.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));
                var back = await again.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
                Assert.True(
                    Vector3.Distance(left, new Vector3(back.X, back.Y, back.Z)) < 0.01f,
                    $"left at {left}, back at ({back.X}, {back.Y}, {back.Z})"
                );
            }
        );
    }

    [Fact]
    public async Task ASavedSpotThatNoLongerFitsFallsBackToTheShapesSpawn()
    {
        await WithServer(
            null,
            async url =>
            {
                var id = Character("Ada");
                _db.SetPosition(id, new CharacterPosition(9999, 1, 2, 3, 0)); // a zone that's gone

                using var ada = new RawClient();
                await ada.ConnectAsync(url);
                await ada.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));

                var (zone, home) = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]);
                Assert.Equal(zone.Number, (await ada.NextAsync<ZoneEntered>(ShapeLandMessageIds.ZoneEntered)).Zone);
                var joined = await ada.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
                Assert.True(Comet.Simulation.Triggers.Inside(home.Volume, new Vector3(joined.X, joined.Y, joined.Z)));
            }
        );
    }

    [Fact]
    public async Task PlayersStartAtTheStartSpawnInItsZone()
    {
        using var ada = new RawClient();
        await ada.ConnectAsync(_url);
        await ada.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));

        var (zone, start) = ShapeLandWorld.FindSpawn(_content, StartSpawn)!.Value;
        Assert.Equal(zone.Number, (await ada.NextAsync<Welcome>(MessageIds.Welcome)).Zone);
        Assert.Equal(zone.Number, (await ada.NextAsync<ZoneEntered>(ShapeLandMessageIds.ZoneEntered)).Zone);
        var joined = await ada.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
        Assert.True(Comet.Simulation.Triggers.Inside(start.Volume, new Vector3(joined.X, joined.Y, joined.Z)));
        var set = await ada.NextAsync<SpawnSet>(ShapeLandMessageIds.SpawnSet);
        Assert.Equal((start.Number, false), (set.Spawn, set.Claimed));
    }

    [Fact]
    public async Task AnUnknownStartSpawnStopsTheServerFromStarting()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => StartServer("spawn.nowhere"));
        Assert.Contains("spawn.nowhere", failure.Message);
        Assert.Contains(StartSpawn, failure.Message);
    }

    [Fact]
    public async Task PlayersStartAtTheirShapesSpawnAndCanReturnToIt()
    {
        await WithServer(
            null,
            async url =>
            {
                using var ada = new RawClient();
                await ada.ConnectAsync(url);
                var shape = _content.Shapes.All[0];
                await ada.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Ada"));

                var (zone, home) = ShapeLandWorld.StartOf(_content, shape);
                Assert.Equal(zone.Number, (await ada.NextAsync<ZoneEntered>(ShapeLandMessageIds.ZoneEntered)).Zone);
                var joined = await ada.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
                Assert.Equal(home.Number, (await ada.NextAsync<SpawnSet>(ShapeLandMessageIds.SpawnSet)).Spawn);
                Assert.True(Comet.Simulation.Triggers.Inside(home.Volume, new Vector3(joined.X, joined.Y, joined.Z)));

                await ada.SendAsync(ShapeLandMessageIds.ReturnToSpawn, new ReturnToSpawn());
                var back = await ada.NextAsync<PositionCorrection>(MessageIds.PositionCorrection);
                Assert.Equal(CorrectionReason.Respawn, back.Reason);
                Assert.True(Comet.Simulation.Triggers.Inside(home.Volume, new Vector3(back.X, back.Y, back.Z)));
            }
        );
    }

    [Fact]
    public async Task StandingInAnotherSpawnClaimsIt()
    {
        using var ada = new RawClient();
        var at = await JoinAsync(ada, "Ada");
        Assert.False((await ada.NextAsync<SpawnSet>(ShapeLandMessageIds.SpawnSet)).Claimed); // the start, on joining

        var second = Spawn("spawn.tests.second");
        await ada.WalkAsync(World(TestsZone), at, second.Volume.Position, sequence: 0);

        var set = await ada.NextAsync<SpawnSet>(ShapeLandMessageIds.SpawnSet);
        Assert.Equal((second.Number, true), (set.Spawn, set.Claimed));
    }

    [Fact]
    public async Task TeleportersSendPlayersWithinAZoneAndWaitForAStepOff()
    {
        using var ada = new RawClient();
        var at = await JoinAsync(ada, "Ada");

        await ada.WalkAsync(World(TestsZone), at, Teleporter("teleporter.tests.east").Volume.Position, sequence: 0);

        var sent = await ada.NextAsync<PositionCorrection>(MessageIds.PositionCorrection);
        Assert.Equal(CorrectionReason.Teleport, sent.Reason);
        Assert.True(Comet.Simulation.Triggers.Inside(Teleporter("teleporter.tests.west").Volume, new Vector3(sent.X, sent.Y, sent.Z)));
        // Arriving on the west pad, which targets the east one, doesn't send Ada back.
        Assert.Null(await ada.TryNextAsync<PositionCorrection>(MessageIds.PositionCorrection, TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public async Task TeleportersSendPlayersToAnotherZone()
    {
        using var ada = new RawClient();
        using var bea = new RawClient();
        var at = await JoinAsync(ada, "Ada");
        await JoinAsync(bea, "Bea");
        var adaId = (await bea.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn)).EntityId;

        await ada.WalkAsync(World(TestsZone), at, Teleporter("teleporter.tests.to_far").Volume.Position, sequence: 0);

        var far = _content.Zones["zone.tests_far"];
        var entered = await ada.NextAsync<ZoneEntered>(ShapeLandMessageIds.ZoneEntered);
        Assert.Equal((far.Number, 0), (entered.Zone, entered.Flipped.Length));
        var sent = await ada.NextAsync<PositionCorrection>(MessageIds.PositionCorrection);
        Assert.Equal(CorrectionReason.Teleport, sent.Reason);
        Assert.True(Comet.Simulation.Triggers.Inside(new Comet.Simulation.Triggers(far).Teleporters[0].Volume, new Vector3(sent.X, sent.Y, sent.Z)));
        Assert.Equal(adaId, (await bea.NextAsync<EntityDespawn>(MessageIds.EntityDespawn)).EntityId);

        // Chat is server-wide, and names the sender, who's no longer in view.
        await bea.SendAsync(ShapeLandMessageIds.ChatSend, new ChatSend { Text = "hello?" });
        var heard = await ada.NextAsync<ChatMessage>(ShapeLandMessageIds.ChatMessage);
        Assert.Equal(("Bea", "hello?"), (heard.Name, heard.Text));
    }

    [Fact]
    public async Task TriggersFlipPlacementsForEveryoneInTheZoneAndFlipThemBack()
    {
        using var ada = new RawClient();
        using var bea = new RawClient();
        var at = await JoinAsync(ada, "Ada");
        await JoinAsync(bea, "Bea");

        await ada.WalkAsync(World(TestsZone), at, PlateCenter(), sequence: 0);

        foreach (var client in new[] { ada, bea })
        {
            var flipped = await client.NextAsync<PlacementsFlipped>(ShapeLandMessageIds.PlacementsFlipped);
            Assert.Equal(("door", true), (flipped.Tag, flipped.Flipped));
        }

        // Someone joining now learns the door is open.
        using var cy = new RawClient();
        await cy.ConnectAsync(_url);
        await cy.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs("Cy"));
        Assert.Equal(["door"], (await cy.NextAsync<ZoneEntered>(ShapeLandMessageIds.ZoneEntered)).Flipped);

        // The plate's effect flips the door back after its revert time (3 s), however long Ada stands on it.
        var back = await bea.NextAsync<PlacementsFlipped>(ShapeLandMessageIds.PlacementsFlipped);
        Assert.Equal(("door", false), (back.Tag, back.Flipped));
    }

    [Fact]
    public async Task APlayerInsideAPlacementThatTurnsSolidPassesThroughIt()
    {
        using var ada = new RawClient();
        var at = await JoinAsync(ada, "Ada");
        var world = World(TestsZone);
        at = await ada.WalkAsync(world, at, PlateCenter(), sequence: 0);
        await ada.NextAsync<PlacementsFlipped>(ShapeLandMessageIds.PlacementsFlipped);
        world.SetFlipped("door", true);

        // Into the open doorway, and wait there for the door to close around Ada.
        at = await ada.WalkAsync(world, at, new Vector3(18, 0, 12), sequence: 0);
        Assert.False((await ada.NextAsync<PlacementsFlipped>(ShapeLandMessageIds.PlacementsFlipped)).Flipped);
        world.SetFlipped("door", false);

        // Walking out through it isn't snapped back.
        await ada.WalkAsync(world, at, new Vector3(21, 0, 12), sequence: 0, passThrough: new HashSet<int>(world.MeshesTagged("door")));
        Assert.Null(await ada.TryNextAsync<PositionCorrection>(MessageIds.PositionCorrection, TimeSpan.FromSeconds(0.5)));
    }

    private SpawnPoint Spawn(string key) => ShapeLandWorld.FindSpawn(_content, key)!.Value.Spawn;

    private Teleporter Teleporter(string key) => _content.Zones.All.SelectMany(z => z.Chunks).SelectMany(c => c.Teleporters).Single(t => t.Id == key);

    private Vector3 PlateCenter() =>
        _content.Zones[TestsZone].Chunks.SelectMany(c => c.Triggers).Single(t => t.Id == "trigger.tests.plate").Volume.Position;

    // A fresh copy of a zone's world, for working out the ground to walk on.
    private Comet.Simulation.CollisionWorld World(string zone) => ShapeLandWorld.Create(_content, _content.Zones[zone]);

    // Connects and joins as name on the tests' server; where they arrived.
    private async Task<Vector3> JoinAsync(RawClient client, string name)
    {
        await client.ConnectAsync(_url);
        await client.SendAsync(ShapeLandMessageIds.JoinRequest, JoinAs(name));
        while (true)
        {
            var spawn = await client.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
            if (spawn.Name == name)
            {
                return new Vector3(spawn.X, spawn.Y, spawn.Z);
            }
        }
    }

    // Runs test against a second server starting players at startSpawn (null: each at their shape's), then stops it.
    private async Task WithServer(string? startSpawn, Func<Uri, Task> test)
    {
        var (server, url) = await StartServer(startSpawn);
        try
        {
            await test(url);
        }
        finally
        {
            await server.StopAsync();
            await server.DisposeAsync();
        }
    }

    /// <summary>A bare WebSocket client, for tests that send and read single messages.</summary>
    internal sealed class RawClient : IDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly Queue<(ushort Id, ReadOnlyMemory<byte> Payload)> _received = new();

        public Task ConnectAsync(Uri url) => _socket.ConnectAsync(url, TestContext.Current.CancellationToken);

        /// <summary>Whether the server closes or drops the connection within <paramref name="wait"/>.</summary>
        public Task<bool> ClosedAsync(TimeSpan wait) => Dropped(_socket, wait);

        public async Task SendAsync<T>(ushort messageId, T message)
        {
            var frame = new MessageWriter(options: ShapeLandProtocol.Options);
            frame.BeginFrame(0);
            frame.Write(messageId, message);
            await _socket.SendAsync(frame.WrittenMemory, WebSocketMessageType.Binary, endOfMessage: true, TestContext.Current.CancellationToken);
        }

        /// <summary>Skips messages until one with <paramref name="messageId"/> arrives.</summary>
        public async Task<T> NextAsync<T>(ushort messageId)
            where T : struct => (await TryNextAsync<T>(messageId, TimeSpan.FromSeconds(8), throwOnTimeout: true))!.Value;

        /// <summary>Skips messages until one with <paramref name="messageId"/> arrives, or <paramref name="wait"/> passes (then null).</summary>
        public async Task<T?> TryNextAsync<T>(ushort messageId, TimeSpan wait, bool throwOnTimeout = false)
            where T : struct
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(wait);
            try
            {
                return await Next<T>(messageId, timeout.Token);
            }
            catch (OperationCanceledException) when (!throwOnTimeout && !TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        /// <summary>
        /// Walks from <paramref name="from"/> to <paramref name="to"/> at 5 m/s over the ground of <paramref name="world"/>,
        /// reporting every 50 ms as a client would, echoing correction <paramref name="sequence"/>; where it ended.
        /// </summary>
        public async Task<Vector3> WalkAsync(
            Comet.Simulation.CollisionWorld world,
            Vector3 from,
            Vector3 to,
            uint sequence,
            ISet<int>? passThrough = null
        )
        {
            const float speed = 5,
                step = 0.05f;
            var across = new Vector2(to.X - from.X, to.Z - from.Z);
            var steps = Math.Max(1, (int)MathF.Ceiling(across.Length() / (speed * step)));
            var velocity = across.Length() > 0 ? Vector2.Normalize(across) * speed : Vector2.Zero;
            var at = from;
            for (var i = 1; i <= steps; i++)
            {
                var flat = Vector2.Lerp(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z), (float)i / steps);
                world.TryGetGround(new Vector3(flat.X, at.Y + 1, flat.Y), 0.4f, at.Y + 1, out var ground, 0, passThrough);
                at = new Vector3(flat.X, ground, flat.Y);
                var moving = i < steps ? velocity : Vector2.Zero;
                await SendAsync(
                    MessageIds.PositionReport,
                    new PositionReport
                    {
                        X = at.X,
                        Y = at.Y,
                        Z = at.Z,
                        VelocityX = moving.X,
                        VelocityZ = moving.Y,
                        Facing = MathF.Atan2(velocity.X, velocity.Y),
                        CorrectionSequence = sequence,
                    }
                );
                await Task.Delay(TimeSpan.FromSeconds(step), TestContext.Current.CancellationToken);
            }

            return at;
        }

        /// <summary>Skips messages until one with any of <paramref name="messageIds"/> arrives; its id and payload.</summary>
        public async Task<(ushort Id, ReadOnlyMemory<byte> Payload)> NextOfAsync(params ushort[] messageIds)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            return await NextOf(messageIds, timeout.Token);
        }

        private async Task<T> Next<T>(ushort messageId, CancellationToken cancel) =>
            FrameReader.Decode<T>((await NextOf([messageId], cancel)).Payload, ShapeLandProtocol.Options);

        private async Task<(ushort Id, ReadOnlyMemory<byte> Payload)> NextOf(ushort[] messageIds, CancellationToken cancel)
        {
            while (true)
            {
                while (_received.TryDequeue(out var message))
                {
                    if (messageIds.Contains(message.Id))
                    {
                        return message;
                    }
                }

                var buffer = new byte[FrameReader.MaxServerFrameSize];
                var result = await _socket.ReceiveAsync(buffer, cancel);
                Assert.True(result.EndOfMessage);
                var reader = FrameReader.Create(buffer.AsMemory(0, result.Count));
                while (reader.TryReadNext(out var id, out var payload))
                {
                    _received.Enqueue((id, payload));
                }
            }
        }

        public void Dispose() => _socket.Dispose();
    }

    [Fact]
    public async Task StoppingTheServerClosesConnectionsPromptly()
    {
        // Rather than leaving clients frozen until the host's shutdown timeout.
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_url, TestContext.Current.CancellationToken);
        var buffer = new byte[64 * 1024];
        var receiving = Task.Run(
            async () =>
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, TestContext.Current.CancellationToken);
                        return result.CloseStatus;
                    }
                }
            },
            TestContext.Current.CancellationToken
        );

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await _server.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"stopping took {stopwatch.Elapsed}");
        Assert.Equal(
            WebSocketCloseStatus.EndpointUnavailable,
            await receiving.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task AFloodOfMessagesDropsTheClient()
    {
        // Far more pings than any honest client sends, all at once: the server drops the connection rather than
        // answering them all.
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_url, TestContext.Current.CancellationToken);
        var frame = new MessageWriter();
        frame.BeginFrame(0);
        for (var i = 0; i < 300; i++)
        {
            frame.Write(MessageIds.Ping, new Ping { ClientTime = i });
        }

        await socket.SendAsync(frame.WrittenMemory, WebSocketMessageType.Binary, endOfMessage: true, TestContext.Current.CancellationToken);

        Assert.True(await Dropped(socket, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task OneAddressCanOpenOnlySoManyConnections()
    {
        var limit = new ConnectionLimits().MaxConnectionsPerAddress;
        var sockets = new List<ClientWebSocket>();
        try
        {
            for (var i = 0; i < limit; i++)
            {
                sockets.Add(new ClientWebSocket());
                await sockets[^1].ConnectAsync(_url, TestContext.Current.CancellationToken);
            }

            using var extra = new ClientWebSocket();
            await Assert.ThrowsAsync<WebSocketException>(() => extra.ConnectAsync(_url, TestContext.Current.CancellationToken));

            // A place frees up when a connection closes.
            sockets[0].Abort();
            await Task.Delay(200, TestContext.Current.CancellationToken);
            using var again = new ClientWebSocket();
            await again.ConnectAsync(_url, TestContext.Current.CancellationToken);
        }
        finally
        {
            sockets.ForEach(socket => socket.Dispose());
        }
    }

    [Fact]
    public async Task ForwardedAddressesFromThisMachineCountPerPlayer()
    {
        // Through a proxy on this machine every connection arrives from loopback; X-Forwarded-For tells players apart,
        // so more than the per-address cap can connect in total, while each player is still capped.
        var limit = new ConnectionLimits().MaxConnectionsPerAddress;
        var sockets = new List<ClientWebSocket>();
        try
        {
            for (var i = 0; i < limit + 2; i++)
            {
                var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("X-Forwarded-For", $"203.0.113.{i}");
                sockets.Add(socket);
                await socket.ConnectAsync(_url, TestContext.Current.CancellationToken);
            }

            for (var i = 0; i < limit; i++)
            {
                var same = new ClientWebSocket();
                same.Options.SetRequestHeader("X-Forwarded-For", "198.51.100.7");
                sockets.Add(same);
                await same.ConnectAsync(_url, TestContext.Current.CancellationToken);
            }

            using var extra = new ClientWebSocket();
            extra.Options.SetRequestHeader("X-Forwarded-For", "198.51.100.7");
            await Assert.ThrowsAsync<WebSocketException>(() => extra.ConnectAsync(_url, TestContext.Current.CancellationToken));
        }
        finally
        {
            sockets.ForEach(socket => socket.Dispose());
        }
    }

    [Fact]
    public async Task InfoGivesTheContentHashToAnyPage()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync(new Uri(_server.Urls.First() + "/info"), TestContext.Current.CancellationToken);

        var hash = ContentHash.Of(File.ReadAllBytes(_contentFile));
        Assert.Equal($"{{\"contentHash\":\"{hash}\"}}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task HealthSaysOkToAnyPage()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync(new Uri(_server.Urls.First() + "/health"), TestContext.Current.CancellationToken);

        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task AConnectionThatNeverJoinsIsDropped()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_url, TestContext.Current.CancellationToken);

        Assert.False(await Dropped(socket, TimeSpan.FromSeconds(ShapeLandRules.JoinDeadlineSeconds - 1)));
        Assert.True(await Dropped(socket, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ASecondJoinRequestDropsTheClient()
    {
        // One join per connection: no honest client sends another.
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(_url, TestContext.Current.CancellationToken);
        var frame = new MessageWriter(options: ShapeLandProtocol.Options);
        frame.BeginFrame(0);
        for (var i = 0; i < 2; i++)
        {
            frame.Write(ShapeLandMessageIds.JoinRequest, new JoinRequest { Ticket = "x", ContentFingerprint = _content.Fingerprint });
        }

        await socket.SendAsync(frame.WrittenMemory, WebSocketMessageType.Binary, endOfMessage: true, TestContext.Current.CancellationToken);

        Assert.True(await Dropped(socket, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RetryingFailedJoinsTooFastDropsTheClient()
    {
        // Each retry checks a ticket, so a client can't retry without limit on one connection.
        using var client = new RawClient();
        await client.ConnectAsync(_url);
        for (var i = 0; i < 5; i++)
        {
            await client.SendAsync(
                ShapeLandMessageIds.JoinRequest,
                new JoinRequest { Ticket = "not a ticket", ContentFingerprint = _content.Fingerprint }
            );
            Assert.Equal(JoinRejection.BadTicket, (await client.NextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected)).Reason);
        }

        // A burst, so a refill while the round trips above ran slowly can't let every retry through.
        try
        {
            for (var i = 0; i < 5; i++)
            {
                await client.SendAsync(
                    ShapeLandMessageIds.JoinRequest,
                    new JoinRequest { Ticket = "not a ticket", ContentFingerprint = _content.Fingerprint }
                );
            }
        }
        catch (WebSocketException)
        {
            // already dropped
        }

        Assert.True(await client.ClosedAsync(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Reads until the server closes or drops the connection, or <paramref name="wait"/> passes.</summary>
    private static async Task<bool> Dropped(ClientWebSocket socket, TimeSpan wait)
    {
        var buffer = new byte[64 * 1024];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return true;
                }
            }
        }
        catch (WebSocketException)
        {
            return true; // aborted without a close frame
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root.");
    }
}
