using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Comet.Data.Accounts;
using Comet.Data.Characters;
using Comet.Protocol.Login;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Comet.Login.Tests.PostgresContainer))]

namespace Comet.Login.Tests;

public class TestCharacter : Character
{
    public required int Look { get; set; }
}

public sealed class TestRegionContext(DbContextOptions<TestRegionContext> options) : CometRegionContext<TestCharacter>(options);

public sealed class TestAccountContext(DbContextOptions<TestAccountContext> options) : CometAccountContext(options);

/// <summary>A test game's rules: ShapeLand-like names, a look number, one game server.</summary>
public sealed partial class TestRules : ILoginRules<TestCharacter>
{
    public const int ServerId = 7;
    public const string ServerAddress = "ws://game.test/ws";

    public string? CheckName(string name) => NameRule().IsMatch(name) ? null : LoginErrors.InvalidName;

    public TestCharacter? CreateCharacter(long characterId, long accountId, string username, JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty("look", out var look) && look.GetInt32() is >= 0 and < 10
            ? new TestCharacter { Id = characterId, AccountId = accountId, Name = username, Look = look.GetInt32() }
            : null;

    public object Describe(TestCharacter character) => new TestView { Name = character.Name, Look = character.Look };

    public GameServerAddress ServerFor(TestCharacter character) => new(ServerId, ServerAddress);

    public bool CharacterNamesFollowUsername => true;
    public bool CharactersDeletedWithAccount => true;

    [GeneratedRegex("^[A-Za-z0-9_-]{3,16}$")]
    private static partial Regex NameRule();
}

public sealed class TestView
{
    public string Name { get; set; } = "";
    public int Look { get; set; }
}

/// <summary>One throwaway PostgreSQL 18 container for the run (Testcontainers; needs Docker).</summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}

/// <summary>
/// A Login server in memory (TestServer) on a fresh database, with a fake clock. Requests come from
/// <see cref="ClientAddressHeader"/>'s address (default 127.0.0.1, which counts as the trusted local proxy).
/// </summary>
public sealed class LoginHost : IAsyncDisposable
{
    public const string ClientAddressHeader = "X-Test-Client-Address";

    private readonly WebApplication _app;
    private readonly string _keys;

    private LoginHost(WebApplication app, string keys, FakeTimeProvider clock, string connection)
    {
        _app = app;
        _keys = keys;
        Clock = clock;
        Connection = connection;
    }

    public FakeTimeProvider Clock { get; }
    public string Connection { get; }
    public string PublicKeys => Path.Combine(_keys, "public");
    public IServiceProvider Services => _app.Services;
    public AccountActions<TestCharacter> Actions => Services.GetRequiredService<AccountActions<TestCharacter>>();

    public static async Task<LoginHost> StartAsync(PostgresContainer postgres, Action<LoginOptions>? configure = null)
    {
        var connection = await postgres.CreateDatabaseAsync();
        await CreateTablesAsync(connection);

        var keys = Path.Combine(Path.GetTempPath(), $"comet-login-tests-{Guid.NewGuid():N}");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddCometLogin<TestAccountContext, TestRegionContext, TestCharacter, TestRules>(options =>
        {
            options.ConnectionString = connection;
            options.NodeId = 1;
            options.SigningKeyFile = Path.Combine(keys, "login.pem");
            options.PublicKeysFolder = Path.Combine(keys, "public");
            options.CreateSigningKey = true;
            options.Migrate = false; // the test contexts have no migrations: their tables are made directly
            options.GuestIdle = TimeSpan.FromDays(7);
            options.AccountHold = TimeSpan.FromDays(7);
            options.CharacterHold = TimeSpan.FromDays(7);
            options.AllowedOrigins = ["https://page.test"];
            options.RateLimits.SignInPerMinute = 1000;
            options.RateLimits.GuestsPerHour = 1000;
            options.RateLimits.OtherPerMinute = 1000;
            configure?.Invoke(options);
        });

        var app = builder.Build();
        app.Use((http, next) =>
        {
            http.Connection.RemoteIpAddress = IPAddress.Parse(
                http.Request.Headers.TryGetValue(ClientAddressHeader, out var address) ? address.ToString() : "127.0.0.1");
            return next(http);
        });
        app.MapCometLogin<TestCharacter>();
        await app.StartAsync();
        return new LoginHost(app, keys, clock, connection);
    }

    /// <summary>A client with no session yet, as if on another device.</summary>
    public Client NewClient(string? address = null)
    {
        var http = _app.GetTestClient();
        if (address != null)
        {
            http.DefaultRequestHeaders.Add(ClientAddressHeader, address);
        }

        return new Client(http);
    }

    /// <summary>A new guest with its own client, signed in.</summary>
    public async Task<Client> GuestAsync(string name, int look = 1)
    {
        var client = NewClient();
        var response = await client.PostAsync(LoginPaths.Guest, new { name, character = new { look } });
        response.EnsureSuccessStatusCode();
        client.Token = (await response.Content.ReadFromJsonAsync<SessionResponse>())!.Token;
        return client;
    }

    public TestAccountContext Accounts()
    {
        var options = new DbContextOptionsBuilder<TestAccountContext>();
        options.UseCometAccount(Connection, Clock);
        return new(options.Options);
    }

    public TestRegionContext Region()
    {
        var options = new DbContextOptionsBuilder<TestRegionContext>();
        Comet.Data.CometDatabase.UseCometRegion(options, Connection, Clock);
        return new(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(_keys))
        {
            Directory.Delete(_keys, recursive: true);
        }
    }

    private static async Task CreateTablesAsync(string connection)
    {
        var accountOptions = new DbContextOptionsBuilder<TestAccountContext>();
        accountOptions.UseCometAccount(connection, TimeProvider.System);
        await using (var accounts = new TestAccountContext(accountOptions.Options))
        {
            await accounts.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        var regionOptions = new DbContextOptionsBuilder<TestRegionContext>();
        Comet.Data.CometDatabase.UseCometRegion(regionOptions, connection, TimeProvider.System);
        await using var region = new TestRegionContext(regionOptions.Options);
        await region.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    /// <summary>One device: an HTTP client that keeps its session token.</summary>
    public sealed class Client(HttpClient http)
    {
        public string? Token { get; set; }
        public HttpClient Http => http;

        public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

        public Task<HttpResponseMessage> PostAsync(string path, object? body = null) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Post, path) { Content = body == null ? null : JsonContent.Create(body) });

        public async Task<T> PostAsync<T>(string path, object? body = null)
        {
            var response = await PostAsync(path, body);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }

        /// <summary>Signs in with a password, keeping the new token. Returns the error code, or null.</summary>
        public async Task<string?> SignInAsync(string username, string password, string path = LoginPaths.SignIn)
        {
            var response = await PostAsync(path, new { username, password });
            if (!response.IsSuccessStatusCode)
            {
                return await ErrorAsync(response);
            }

            Token = (await response.Content.ReadFromJsonAsync<SessionResponse>())!.Token;
            return null;
        }

        public async Task<string?> SignInWithCodeAsync(string code)
        {
            var response = await PostAsync(LoginPaths.SignInWithCode, new { code });
            if (!response.IsSuccessStatusCode)
            {
                return await ErrorAsync(response);
            }

            Token = (await response.Content.ReadFromJsonAsync<SessionResponse>())!.Token;
            return null;
        }

        public async Task<AccountResponse<TestView>> MeAsync()
        {
            var response = await GetAsync(LoginPaths.Me);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AccountResponse<TestView>>())!;
        }

        /// <summary>The error code of a call (null if it succeeded).</summary>
        public async Task<string?> ErrorOfAsync(Task<HttpResponseMessage> call) => await ErrorAsync(await call);

        public static async Task<string?> ErrorAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            return problem.GetProperty("code").GetString();
        }

        private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (Token != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            }

            return http.SendAsync(request);
        }
    }
}
