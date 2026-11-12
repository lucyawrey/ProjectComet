using Comet.Client.Login;
using Comet.Data;
using Comet.Data.Characters;
using Comet.Protocol.Login;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;

[assembly: AssemblyFixture(typeof(Comet.Testing.PostgresContainer))]

namespace ShapeLand.LoginServer.Tests;

public sealed class LoginServerTests(PostgresContainer postgres) : IAsyncDisposable
{
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"shapeland-login-tests-{Guid.NewGuid():N}");
    private WebApplication? _app;

    private sealed class Tokens : ITokenStore
    {
        private string? _token;

        public string? Load() => _token;

        public void Save(string? token) => _token = token;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_keys))
        {
            Directory.Delete(_keys, recursive: true);
        }
    }

    [Fact]
    public async Task AGuestStartsWithALookTheContentAllows()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var app = await StartAsync(connection);
        var content = app.Services.GetRequiredService<ShapeLandContent>();
        var shape = content.Shapes.All[0].Number;
        var color = content.Colors.All[1].Number;

        var client = Client(app);
        Assert.Equal(LoginErrors.InvalidName, (await client.CreateGuestAsync("has space", Look(shape, color))).Error);
        Assert.Equal(LoginErrors.InvalidCharacter, (await client.CreateGuestAsync("Alice", Look(9999, color))).Error);
        Assert.Equal(LoginErrors.InvalidCharacter, (await client.CreateGuestAsync("Alice", Look(shape, 9999))).Error);
        Assert.True((await client.CreateGuestAsync("Alice", Look(shape, color))).Succeeded);

        var me = await client.GetAccountAsync();
        Assert.Equal(
            ("Alice", shape, color, color),
            (me.Value!.Character!.Name, me.Value.Character.Shape, me.Value.Character.Color, me.Value.Character.EyeColor)
        );
        Assert.Equal("ws://game.test/ws", (await client.GetTicketAsync()).Value!.Server);
    }

    [Fact]
    public async Task AGameServerStartedFirstWaitsForTheMigrations()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var repository = new CharacterRepository<ShapeLandCharacter, ShapeLandRegionContext>(
            () => Region(connection),
            new CharacterRepositoryOptions { ServerId = 1 },
            TimeProvider.System,
            NullLogger.Instance
        );
        var starting = repository.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Assert.False(starting.IsCompleted);

        var app = await StartAsync(connection);
        await starting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var content = app.Services.GetRequiredService<ShapeLandContent>();
        var client = Client(app);
        Assert.True((await client.CreateGuestAsync("Bob", Look(content.Shapes.All[0].Number, content.Colors.All[0].Number))).Succeeded);
        await using var region = Region(connection);
        var id = (await region.Characters.SingleAsync(TestContext.Current.CancellationToken)).Id;
        Assert.Equal(ClaimOutcome.Claimed, (await repository.ClaimAsync(id, TestContext.Current.CancellationToken)).Outcome);
    }

    private static NewCharacter Look(int shape, int color) =>
        new()
        {
            Shape = shape,
            Color = color,
            EyeColor = color,
        };

    private static LoginClient<CharacterView> Client(WebApplication app) => new(new HttpLoginTransport(app.GetTestClient()), new Tokens());

    private static ShapeLandRegionContext Region(string connection)
    {
        var options = new DbContextOptionsBuilder<ShapeLandRegionContext>();
        options.UseCometRegion(connection, TimeProvider.System);
        return new(options.Options);
    }

    private async Task<WebApplication> StartAsync(string connection)
    {
        _app = ShapeLandLogin.Build(
            [],
            builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Login:ConnectionString"] = connection,
                        ["Login:SigningKeyFile"] = Path.Combine(_keys, "login.pem"),
                        ["Login:PublicKeysFolder"] = Path.Combine(_keys, "public"),
                        ["Login:CreateSigningKey"] = "true",
                        ["ShapeLand:GameServer:Address"] = "ws://game.test/ws",
                    }
                );
            }
        );
        await _app.StartAsync();
        return _app;
    }
}
