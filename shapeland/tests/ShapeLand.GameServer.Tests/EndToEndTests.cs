using Microsoft.AspNetCore.Builder;
using ShapeLand.Bots;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer.Tests;

/// <summary>Runs the real server in-process on a free port, with bots connecting over WebSockets.</summary>
public sealed class EndToEndTests : IAsyncLifetime
{
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

        _server = ShapeLandServer.Build([], builder =>
        {
            builder.Configuration["ShapeLand:ContentFile"] = _contentFile;
            builder.Configuration["Urls"] = "http://127.0.0.1:0";
        });
        await _server.StartAsync(TestContext.Current.CancellationToken);
        _url = new Uri(_server.Urls.First().Replace("http://", "ws://") + "/ws");
    }

    public async ValueTask DisposeAsync()
    {
        await _server.StopAsync();
        await _server.DisposeAsync();
        File.Delete(_contentFile);
    }

    private Bot NewBot(string name, int seed, BotSettings settings) =>
        new(name, seed, settings, _content, ShapeLandWorld.Create(_content));

    [Fact]
    public async Task PlayersSeeEachOtherChatAndCheatersGetSnappedBack()
    {
        var honest = Enumerable.Range(1, 3).Select(i => NewBot($"Honest {i}", i, new BotSettings { ChatEverySeconds = 1 })).ToList();
        var cheater = NewBot("Cheater", 99, new BotSettings { ChatEverySeconds = 0, SpeedCheat = 2.5f });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(4));

        await Task.WhenAll(honest.Append(cheater).Select(bot => bot.RunAsync(_url, stop.Token)));

        Assert.All(honest.Append(cheater), bot =>
        {
            Assert.False(bot.Failed, $"{bot.Name} failed");
            Assert.True(bot.Joined, $"{bot.Name} didn't join");
            Assert.Equal(3, bot.SeenPlayers.Count);
            Assert.True(bot.StatesReceived > 30, $"{bot.Name} got only {bot.StatesReceived} position updates");
        });
        Assert.All(honest, bot =>
        {
            Assert.Equal(0, bot.SnapBacks);
            Assert.True(bot.ChatsReceived >= honest.Sum(b => b.ChatsSent) - 1, $"{bot.Name} heard {bot.ChatsReceived} chat lines");
        });
        Assert.True(cheater.SnapBacks > 0, "the cheater was never snapped back");
    }

    [Fact]
    public async Task NamesMustBeUniqueOnline()
    {
        var first = NewBot("Ada", 1, new BotSettings { ChatEverySeconds = 0 });
        var second = NewBot("ada", 2, new BotSettings { ChatEverySeconds = 0 });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(2));

        var running = first.RunAsync(_url, stop.Token);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await Task.WhenAll(running, second.RunAsync(_url, stop.Token));

        Assert.True(first.Joined);
        Assert.False(second.Joined);
        Assert.Equal(Shared.Messages.JoinRejection.NameTaken, second.Rejected);
    }

    [Fact]
    public async Task ColoursMustBeAvailableToThePlayer()
    {
        var bot = NewBot("Ada", 1, new BotSettings { ChatEverySeconds = 0, Colour = 0x123456 });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(1));

        await bot.RunAsync(_url, stop.Token);

        Assert.False(bot.Joined);
        Assert.Equal(Shared.Messages.JoinRejection.UnavailableColour, bot.Rejected);
    }

    private static string RepoRoot()
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
