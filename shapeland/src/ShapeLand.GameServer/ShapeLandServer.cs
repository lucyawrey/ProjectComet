using Comet.Content;
using Comet.Data;
using Comet.Data.Characters;
using Comet.Server;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Comet.Tickets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ShapeLand.Data;
using ShapeLand.Shared;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

public static class ShapeLandServer
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        configure?.Invoke(builder);
        builder.AddCometLogFile();

        builder.Services.Configure<ShapeLandServerOptions>(builder.Configuration.GetSection("ShapeLand"));
        builder.Services.AddSingleton(services => LoadContent(services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value));
        builder.Services.AddSingleton(services => services.GetRequiredService<LoadedContent>().Content);
        builder.Services.AddSingleton(new ConnectionRegistry(ShapeLandProtocol.Options));
        builder.Services.AddSingleton(builder.Configuration.GetSection("Comet:Connections").Get<ConnectionLimits>() ?? new ConnectionLimits());
        builder.Services.TryAddSingleton(TimeProvider.System);

        var devJoins = builder.Configuration.GetValue<bool>("ShapeLand:DevJoins");
#if DEBUG
        if (devJoins)
        {
            AddDevCharacters(builder);
        }
#else
        if (devJoins)
        {
            throw new InvalidOperationException("ShapeLand:DevJoins is for development: only a Debug build of the game server takes it.");
        }
#endif

        builder.Services.TryAddSingleton<ICharacterRepository<ShapeLandCharacter>>(services =>
        {
            var options = services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
            if (string.IsNullOrEmpty(options.ConnectionString))
            {
                throw new InvalidOperationException("Set ShapeLand:ConnectionString (the region database, which the Login server migrates).");
            }

            var clock = services.GetRequiredService<TimeProvider>();
            return new CharacterRepository<ShapeLandCharacter, ShapeLandRegionContext>(
                () =>
                {
                    var context = new DbContextOptionsBuilder<ShapeLandRegionContext>();
                    context.UseCometRegion(options.ConnectionString, clock);
                    return new ShapeLandRegionContext(context.Options);
                },
                new CharacterRepositoryOptions { ServerId = options.ServerId, SaveInterval = TimeSpan.FromSeconds(options.SaveIntervalSeconds) },
                clock,
                services.GetRequiredService<ILogger<ICharacterRepository<ShapeLandCharacter>>>()
            );
        });
        builder.Services.TryAddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
            var folder = string.IsNullOrEmpty(options.TicketKeysFolder)
                ? Path.Combine(RepoPaths.Root("Set ShapeLand:ContentFile."), "artifacts", "keys", "public")
                : options.TicketKeysFolder;
            var keys = Directory.Exists(folder) ? TicketKeys.PublicKeysIn(folder) : [];
            if (keys.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No ticket keys in {folder}: start the Login server first (it makes them in development), or set ShapeLand:TicketKeysFolder."
                );
            }

            return new TicketChecker(keys, options.ServerId, services.GetRequiredService<TimeProvider>());
        });

        builder.Services.AddSingleton(services => new ShapeLandGame(
            services.GetRequiredService<ShapeLandContent>(),
            services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.StartSpawn,
            services.GetRequiredService<ConnectionRegistry>(),
            devJoins ? null : services.GetRequiredService<TicketChecker>(),
            services.GetRequiredService<ICharacterRepository<ShapeLandCharacter>>(),
            services.GetRequiredService<ILogger<ShapeLandGame>>()
        )
        {
#if DEBUG
            DevCharacters = services.GetService<DevCharacters>(),
#endif
        });
        builder.Services.AddSingleton<IConnectionHandler>(services => services.GetRequiredService<ShapeLandGame>());
        builder.Services.AddSingleton(services => new TickLoop(ShapeLandRules.TickRate, services.GetRequiredService<ShapeLandGame>().Tick));
        builder.Services.AddHostedService<GameStartup>();

        var app = builder.Build();
        // Behind the local Caddy proxy the client's address comes from X-Forwarded-For, so the per-address cap counts players.
        // Only a loopback proxy is trusted (ASP.NET's default), so no client can set the header.
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
        var serverOptions = app.Services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
        if (!string.IsNullOrEmpty(serverOptions.WebRoot))
        {
            // The Content Security Policy must stay in step with shapeland/deploy/vercel.json, which gives the deployed game's.
            var policy = WebBuild.PagePolicy(serverOptions.LoginUrl);
            app.Use(
                async (context, next) =>
                {
                    context.Response.Headers.ContentSecurityPolicy = policy;
                    await next();
                }
            );

            var config = $"window.shapelandLogin = {System.Text.Json.JsonSerializer.Serialize(serverOptions.LoginUrl)};\n";
            app.MapGet(
                "/config.js",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-cache";
                    return Results.Text(config, "text/javascript");
                }
            );

            if (!File.Exists(Path.Combine(serverOptions.WebRoot, "index.html")))
            {
                throw new FileNotFoundException("No web build to serve (ShapeLand:WebRoot has no index.html).", serverOptions.WebRoot);
            }

            WebBuild.Serve(app, serverOptions.WebRoot);
        }

        app.UseWebSockets();
        app.MapCometWebSocket("/ws");

        app.MapGet(
            "/health",
            (HttpContext context) =>
            {
                context.Response.Headers.AccessControlAllowOrigin = "*";
                return "ok";
            }
        );

        var contentHash = app.Services.GetRequiredService<LoadedContent>().Hash;
        app.MapGet(
            "/info",
            (HttpContext context) =>
            {
                context.Response.Headers.AccessControlAllowOrigin = "*";
                return Results.Json(new ServerInfo(contentHash));
            }
        );

#if DEBUG
        if (devJoins)
        {
            app.MapPost(
                "/dev/stop",
                (HttpContext context, IHostApplicationLifetime lifetime) =>
                {
                    if (context.Connection.RemoteIpAddress is not { } address || !System.Net.IPAddress.IsLoopback(address))
                    {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }

                    lifetime.StopApplication();
                    return Results.Accepted();
                }
            );
        }
#endif

        app.Services.GetRequiredService<LoadedContent>();
        return app;
    }

#if DEBUG
    private static void AddDevCharacters(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
            var database = new InMemoryCharacterDatabase<ShapeLandCharacter>(services.GetRequiredService<TimeProvider>());
            var logger = services.GetRequiredService<ILogger<DevCharacters>>();
            database.SnapshotFailed += e =>
                logger.LogWarning("Couldn't write {File} ({Problem}); the next change tries again", options.DevCharactersFile, e.Message);
            if (!string.IsNullOrEmpty(options.DevCharactersFile) && database.Load(options.DevCharactersFile) is { } problem)
            {
                logger.LogWarning(
                    "Couldn't read {File} ({Problem}); it was renamed to .bad, and the characters start over",
                    options.DevCharactersFile,
                    problem
                );
            }

            return new DevCharacters(database);
        });
        builder.Services.AddSingleton<ICharacterRepository<ShapeLandCharacter>>(services => new InMemoryCharacterRepository<ShapeLandCharacter>(
            services.GetRequiredService<DevCharacters>().Database,
            new CharacterRepositoryOptions
            {
                ServerId = services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.ServerId,
                SaveInterval = TimeSpan.FromSeconds(5),
            },
            services.GetRequiredService<ILogger<ICharacterRepository<ShapeLandCharacter>>>()
        ));
    }
#endif

    private sealed class GameStartup(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<GameStartup> logger) : IHostedService
    {
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await services.GetRequiredService<ICharacterRepository<ShapeLandCharacter>>().StartAsync(cancellationToken);
            var game = services.GetRequiredService<ShapeLandGame>();
            logger.LogInformation("Playing {Zones}", string.Join(", ", game.Content.Zones.All.Select(z => z.Id)));

            var tickLoop = services.GetRequiredService<TickLoop>();
            tickLoop.Failed += (tick, e) =>
            {
                logger.LogCritical(e, "The game failed on tick {Tick}; stopping the server", tick);
                Environment.ExitCode = 1;
                lifetime.StopApplication();
            };
            lifetime.ApplicationStarted.Register(tickLoop.Start);
            lifetime.ApplicationStopping.Register(tickLoop.Stop);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // ApplicationStopping has stopped the tick loop, so the game's state is safe to read here.
            var game = services.GetRequiredService<ShapeLandGame>();
            var repository = services.GetRequiredService<ICharacterRepository<ShapeLandCharacter>>();
            try
            {
                await game.LeaveAllAsync().WaitAsync(FlushTimeout, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                logger.LogWarning("The final saves didn't finish within {Timeout}", FlushTimeout);
            }

            await repository.StopAsync(FlushTimeout);
        }
    }

    private sealed record ServerInfo(string ContentHash);

    private sealed record LoadedContent(ShapeLandContent Content, string Hash);

    private static LoadedContent LoadContent(ShapeLandServerOptions options)
    {
        var path = RepoPaths.ContentFile(options.ContentFile, "Set ShapeLand:ContentFile.");
        var file = File.ReadAllBytes(path);
        return new LoadedContent(ShapeLandContent.Load(new MemoryStream(file)), ContentHash.Of(file));
    }
}
