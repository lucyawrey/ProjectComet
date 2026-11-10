using System.Numerics;
using Comet.Protocol.Framing;
using Comet.Protocol.Login;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ShapeLand.Bots;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using RawClient = ShapeLand.GameServer.Tests.EndToEndTests.RawClient;

namespace ShapeLand.GameServer.Tests;

public sealed class DevJoinTests : IAsyncLifetime
{
    private readonly string _contentFile = Path.Combine(Path.GetTempPath(), $"shapeland-content-{Guid.NewGuid():N}.bin");
    private readonly string _snapshot = Path.Combine(Path.GetTempPath(), $"shapeland-dev-{Guid.NewGuid():N}", "dev-characters.json");
    private ShapeLandContent _content = null!;

    public async ValueTask InitializeAsync()
    {
        var (content, errors) = ShapeLandBuild.Build(Path.Combine(EndToEndTests.RepoRoot(), "shapeland", "content"), saveRegistry: false);
        Assert.Empty(errors);
        _content = content!;
        await using var file = File.Create(_contentFile);
        _content.Save(file);
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(_contentFile);
        if (Directory.Exists(Path.GetDirectoryName(_snapshot)))
        {
            Directory.Delete(Path.GetDirectoryName(_snapshot)!, recursive: true);
        }
        return ValueTask.CompletedTask;
    }

    private async Task<(WebApplication Server, Uri Url)> StartServer(bool devJoins = true)
    {
        var server = ShapeLandServer.Build(
            [],
            builder =>
            {
                builder.Configuration["ShapeLand:ContentFile"] = _contentFile;
                builder.Configuration["Urls"] = "http://127.0.0.1:0";
                if (devJoins)
                {
                    builder.Configuration["ShapeLand:DevJoins"] = "true";
                    builder.Configuration["ShapeLand:DevCharactersFile"] = _snapshot;
                }
                else
                {
                    TestServices.AddInMemoryCharacters(builder);
                }
            }
        );
        await server.StartAsync(TestContext.Current.CancellationToken);
        return (server, new Uri(server.Urls.First().Replace("http://", "ws://") + "/ws"));
    }

    private static async Task Stop(WebApplication server)
    {
        await server.StopAsync();
        await server.DisposeAsync();
    }

    private DevJoinRequest JoinAs(string name, int shape = 0, bool bot = false) =>
        new()
        {
            Name = name,
            Shape = _content.Shapes.All[shape].Number,
            Color = _content.Colors.All[0].Number,
            EyeColor = _content.Colors.All[0].Number,
            ContentFingerprint = _content.Fingerprint,
            Bot = bot,
        };

    private static async Task<PlayerSpawn> JoinAsync(RawClient client, Uri url, DevJoinRequest request)
    {
        await client.ConnectAsync(url);
        await client.SendAsync(ShapeLandMessageIds.DevJoinRequest, request);
        return await client.NextAsync<PlayerSpawn>(ShapeLandMessageIds.PlayerSpawn);
    }

    private static async Task<JoinRejection> RefusedAsync(RawClient client, Uri url, DevJoinRequest request)
    {
        await client.ConnectAsync(url);
        await client.SendAsync(ShapeLandMessageIds.DevJoinRequest, request);
        return (await client.NextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected)).Reason;
    }

    private static async Task WaitForLeave(WebApplication server, string name)
    {
        var database = server.Services.GetRequiredService<DevCharacters>().Database;
        var id = database.FindOrAdd(name, _ => throw new InvalidOperationException($"No character {name}")).Id;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (database.Owner(id) is { Held: true } && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ANameComesBackInAnyCaseWithItsLookAndSpot()
    {
        var (server, url) = await StartServer();
        try
        {
            Vector3 left;
            using (var ada = new RawClient())
            {
                var joined = await JoinAsync(ada, url, JoinAs("Ada", shape: 1));
                Assert.Equal("Ada", joined.Name);
                Assert.Equal(_content.Shapes.All[1].Number, joined.Shape);
                var (zone, _) = ShapeLandWorld.StartOf(_content, _content.Shapes.All[1]);
                var start = new Vector3(joined.X, joined.Y, joined.Z);
                left = await ada.WalkAsync(ShapeLandWorld.Create(_content, zone), start, start + new Vector3(3, 0, 0), sequence: 0);
            }

            await WaitForLeave(server, "Ada");
            using var again = new RawClient();
            var back = await JoinAsync(again, url, JoinAs("ADA", shape: 0));
            Assert.Equal("Ada", back.Name);
            Assert.Equal(_content.Shapes.All[1].Number, back.Shape);
            Assert.True(
                Vector3.Distance(left, new Vector3(back.X, back.Y, back.Z)) < 0.01f,
                $"left at {left}, back at ({back.X}, {back.Y}, {back.Z})"
            );
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task ANameThatsPlayingIsRefusedEvenWhenBothJoinAtOnce()
    {
        var (server, url) = await StartServer();
        try
        {
            using var first = new RawClient();
            using var second = new RawClient();
            await first.ConnectAsync(url);
            await second.ConnectAsync(url);
            await first.SendAsync(ShapeLandMessageIds.DevJoinRequest, JoinAs("Bea"));
            await second.SendAsync(ShapeLandMessageIds.DevJoinRequest, JoinAs("bea"));

            var answers = await Task.WhenAll(
                first.NextOfAsync(ShapeLandMessageIds.PlayerSpawn, ShapeLandMessageIds.JoinRejected),
                second.NextOfAsync(ShapeLandMessageIds.PlayerSpawn, ShapeLandMessageIds.JoinRejected)
            );
            var refusal = Assert.Single(answers, a => a.Id == ShapeLandMessageIds.JoinRejected);
            Assert.Single(answers, a => a.Id == ShapeLandMessageIds.PlayerSpawn);
            Assert.Equal(JoinRejection.NameOnline, FrameReader.Decode<JoinRejected>(refusal.Payload, ShapeLandProtocol.Options).Reason);
            var database = server.Services.GetRequiredService<DevCharacters>().Database;
            var (id, made) = database.FindOrAdd(
                "BEA",
                id =>
                    new()
                    {
                        Id = id,
                        AccountId = 1,
                        Name = "x",
                        Shape = 0,
                        Color = 0,
                        EyeColor = 0,
                    }
            );
            Assert.False(made);
            Assert.True(database.Owner(id) is { Held: true }, "Neither join holds the character.");
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task BotsAndPeopleKeepTheirOwnNames()
    {
        var (server, url) = await StartServer();
        try
        {
            using (var bot = new RawClient())
            {
                await JoinAsync(bot, url, JoinAs("Bot0001", bot: true));
            }

            using (var person = new RawClient())
            {
                await JoinAsync(person, url, JoinAs("Cyra"));
            }

            var characters = server.Services.GetRequiredService<DevCharacters>();
            var botId = characters.Database.FindOrAdd("Bot0001", _ => throw new InvalidOperationException()).Id;
            Assert.Equal(DevCharacters.BotAccountId, characters.AccountOf(botId));

            using var asPerson = new RawClient();
            Assert.Equal(JoinRejection.NameTaken, await RefusedAsync(asPerson, url, JoinAs("bot0001")));
            using var asBot = new RawClient();
            Assert.Equal(JoinRejection.NameTaken, await RefusedAsync(asBot, url, JoinAs("cyra", bot: true)));
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task TheBotsJoinByName()
    {
        var (server, url) = await StartServer();
        using var stop = new CancellationTokenSource();
        try
        {
            var bot = new Bot(
                "Bot0001",
                1,
                new BotSettings { ChatEverySeconds = 0 },
                _content,
                new ZoneWorlds(_content),
                new DevBotAccount("Bot0001", url, _content, 1)
            );
            var running = bot.RunAsync(stop.Token);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!bot.Joined && !bot.Failed && bot.Rejected == null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(bot.Joined, $"The bot didn't join (rejected: {bot.Rejected}, failed: {bot.LoginFailure}).");
            await stop.CancelAsync();
            await running;
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task ABotWhoseLoginServerIsGoneFailsWithoutThrowing()
    {
        var tokens = Path.GetDirectoryName(_snapshot)!;
        Directory.CreateDirectory(tokens);
        await File.WriteAllTextAsync(Path.Combine(tokens, "Bot0001.token"), "cs_stale", TestContext.Current.CancellationToken);
        var account = new LoginBotAccount("Bot0001", new Uri("http://127.0.0.1:1/"), tokens, _content, 1);

        var (join, failure) = await account.JoinAsync(TestContext.Current.CancellationToken);

        Assert.Null(join);
        Assert.NotNull(failure);
    }

    [Fact]
    public async Task ABotWhoseTicketIsRefusedFailsWithoutThrowing()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["Urls"] = "http://127.0.0.1:0";
        await using var login = builder.Build();
        login.MapGet(LoginPaths.Me, () => Results.Json(new AccountResponse<CharacterView> { Username = "Bot0001" }));
        var ticketsAsked = 0;
        login.MapPost(
            LoginPaths.Ticket,
            () =>
            {
                Interlocked.Increment(ref ticketsAsked);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        );
        await login.StartAsync(TestContext.Current.CancellationToken);
        var tokens = Path.GetDirectoryName(_snapshot)!;
        Directory.CreateDirectory(tokens);
        await File.WriteAllTextAsync(Path.Combine(tokens, "Bot0001.token"), "cs_known", TestContext.Current.CancellationToken);
        var account = new LoginBotAccount("Bot0001", new Uri(login.Urls.First()), tokens, _content, 1);

        try
        {
            var (join, failure) = await account.JoinAsync(TestContext.Current.CancellationToken);

            Assert.Null(join);
            Assert.NotNull(failure);
            Assert.True(ticketsAsked > 0, "The bot never asked for a ticket.");
        }
        finally
        {
            await login.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ARefusedBotStopsOnItsOwn()
    {
        var (server, url) = await StartServer();
        try
        {
            var bot = new Bot(
                " ",
                1,
                new BotSettings { ChatEverySeconds = 0 },
                _content,
                new ZoneWorlds(_content),
                new DevBotAccount(" ", url, _content, 1)
            );
            var running = bot.RunAsync(TestContext.Current.CancellationToken);

            Assert.Same(running, await Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
            Assert.Equal(JoinRejection.BadName, bot.Rejected);
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task ABadNameIsRefused()
    {
        var (server, url) = await StartServer();
        try
        {
            using var client = new RawClient();
            Assert.Equal(JoinRejection.BadName, await RefusedAsync(client, url, JoinAs(" ")));
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task CharactersAndSpotsOutliveTheServerInTheSnapshot()
    {
        var (server, url) = await StartServer();
        Vector3 left;
        try
        {
            using var cyrus = new RawClient();
            var joined = await JoinAsync(cyrus, url, JoinAs("Cyrus"));
            var (zone, _) = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]);
            var start = new Vector3(joined.X, joined.Y, joined.Z);
            left = await cyrus.WalkAsync(ShapeLandWorld.Create(_content, zone), start, start + new Vector3(0, 0, 3), sequence: 0);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }
        finally
        {
            await Stop(server);
        }

        (server, url) = await StartServer();
        try
        {
            using var again = new RawClient();
            var back = await JoinAsync(again, url, JoinAs("cyrus"));
            Assert.Equal("Cyrus", back.Name);
            Assert.True(
                Vector3.Distance(left, new Vector3(back.X, back.Y, back.Z)) < 0.01f,
                $"left at {left}, back at ({back.X}, {back.Y}, {back.Z})"
            );
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task ASnapshotThatWontReadIsSetAside()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_snapshot)!);
        await File.WriteAllTextAsync(_snapshot, "{ not json", TestContext.Current.CancellationToken);
        var (server, url) = await StartServer();
        try
        {
            using var client = new RawClient();
            Assert.Equal("Dee", (await JoinAsync(client, url, JoinAs("Dee"))).Name);
            Assert.Equal("{ not json", await File.ReadAllTextAsync(_snapshot + ".bad", TestContext.Current.CancellationToken));
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task AServerThatNeedsAccountsSaysSo()
    {
        var (server, url) = await StartServer(devJoins: false);
        try
        {
            using var client = new RawClient();
            Assert.Equal(JoinRejection.AccountsNeeded, await RefusedAsync(client, url, JoinAs("Eve")));
        }
        finally
        {
            await Stop(server);
        }
    }

    [Fact]
    public async Task TheEditorCanStopTheServerFromThisComputer()
    {
        var (server, url) = await StartServer();
        try
        {
            var stopping = new TaskCompletionSource();
            server.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
            using var http = new HttpClient();
            var response = await http.PostAsync(server.Urls.First() + "/dev/stop", null, TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            await Stop(server);
        }
    }
}
