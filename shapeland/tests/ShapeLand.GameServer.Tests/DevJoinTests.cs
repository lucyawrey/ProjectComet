using System.Numerics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using RawClient = ShapeLand.GameServer.Tests.EndToEndTests.RawClient;

namespace ShapeLand.GameServer.Tests;

/// <summary>
/// The editor's server without accounts (<c>ShapeLand:DevJoins</c>): joins by name, characters in memory and in a
/// snapshot file that outlives the server. Tests build Debug, where dev joins exist.
/// </summary>
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

    // A dev server on a free port with the tests' snapshot file; or, with devJoins off, one that needs accounts.
    private async Task<(WebApplication Server, Uri Url)> StartServer(bool devJoins = true)
    {
        var server = ShapeLandServer.Build([], builder =>
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
        });
        await server.StartAsync(TestContext.Current.CancellationToken);
        return (server, new Uri(server.Urls.First().Replace("http://", "ws://") + "/ws"));
    }

    private static async Task Stop(WebApplication server)
    {
        await server.StopAsync();
        await server.DisposeAsync();
    }

    private DevJoinRequest JoinAs(string name, int shape = 0) => new()
    {
        Name = name, Shape = _content.Shapes.All[shape].Number, Color = _content.Colors.All[0].Number,
        EyeColor = _content.Colors.All[0].Number, ContentFingerprint = _content.Fingerprint,
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

    // Waits until nobody holds the character called name (its final save is in).
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
            Assert.Equal(_content.Shapes.All[1].Number, back.Shape); // the saved look, not the card's
            Assert.True(Vector3.Distance(left, new Vector3(back.X, back.Y, back.Z)) < 0.01f, $"left at {left}, back at ({back.X}, {back.Y}, {back.Z})");
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

            // Exactly one is refused (a timed-out read aborts its socket, so each client is read only once), and
            // the other holds the one character.
            var refusals = await Task.WhenAll(
                first.TryNextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected, TimeSpan.FromSeconds(5)),
                second.TryNextAsync<JoinRejected>(ShapeLandMessageIds.JoinRejected, TimeSpan.FromSeconds(5)));
            Assert.Single(refusals, r => r != null);
            Assert.Equal(JoinRejection.NameOnline, refusals.Single(r => r != null)!.Value.Reason);
            var database = server.Services.GetRequiredService<DevCharacters>().Database;
            var (id, made) = database.FindOrAdd("BEA", id => new() { Id = id, AccountId = 1, Name = "x", Shape = 0, Color = 0, EyeColor = 0 });
            Assert.False(made);
            Assert.True(database.Owner(id) is { Held: true }, "Neither join holds the character.");
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
            // Still playing when the server stops: the stop writes the spot.
            using var cyrus = new RawClient();
            var joined = await JoinAsync(cyrus, url, JoinAs("Cyrus"));
            var (zone, _) = ShapeLandWorld.StartOf(_content, _content.Shapes.All[0]);
            var start = new Vector3(joined.X, joined.Y, joined.Z);
            left = await cyrus.WalkAsync(ShapeLandWorld.Create(_content, zone), start, start + new Vector3(0, 0, 3), sequence: 0);
            await Task.Delay(200, TestContext.Current.CancellationToken); // the last report reaches the tick
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
            Assert.True(Vector3.Distance(left, new Vector3(back.X, back.Y, back.Z)) < 0.01f, $"left at {left}, back at ({back.X}, {back.Y}, {back.Z})");
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
