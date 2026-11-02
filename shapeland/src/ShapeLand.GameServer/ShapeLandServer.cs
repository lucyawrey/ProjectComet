using Comet.Content;
using Comet.Data;
using Comet.Data.Characters;
using Comet.Server;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Comet.Tickets;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

/// <summary>Builds the ShapeLand game server; used by Program and by tests, which run it in-process.</summary>
public static class ShapeLandServer
{
    /// <summary>The server, ready to run; <paramref name="configure"/> can change the builder first (tests set the port and content).</summary>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        // Settings load from the build output, so the server can be started from any folder.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder);
        builder.AddCometLogFile();

        builder.Services.Configure<ShapeLandServerOptions>(builder.Configuration.GetSection("ShapeLand"));
        builder.Services.AddSingleton(services => LoadContent(services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value));
        builder.Services.AddSingleton(services => services.GetRequiredService<LoadedContent>().Content);
        builder.Services.AddSingleton(new ConnectionRegistry(ShapeLandProtocol.Options));
        builder.Services.AddSingleton(builder.Configuration.GetSection("Comet:Connections").Get<ConnectionLimits>() ?? new ConnectionLimits());
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Characters on PostgreSQL; tests put an in-memory repository and their own ticket keys in first.
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
                services.GetRequiredService<ILogger<ICharacterRepository<ShapeLandCharacter>>>());
        });
        builder.Services.TryAddSingleton(services =>
        {
            var options = services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
            var folder = string.IsNullOrEmpty(options.TicketKeysFolder)
                ? Path.Combine(FindRepoRoot(), "artifacts", "keys", "public")
                : options.TicketKeysFolder;
            var keys = Directory.Exists(folder) ? TicketKeys.PublicKeysIn(folder) : [];
            if (keys.Count == 0)
            {
                throw new InvalidOperationException($"No ticket keys in {folder}: start the Login server first (it makes them in development), or set ShapeLand:TicketKeysFolder.");
            }

            return new TicketChecker(keys, options.ServerId, services.GetRequiredService<TimeProvider>());
        });

        builder.Services.AddSingleton(services => new ShapeLandGame(
            services.GetRequiredService<ShapeLandContent>(),
            services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.StartSpawn,
            services.GetRequiredService<ConnectionRegistry>(),
            services.GetRequiredService<TicketChecker>(),
            services.GetRequiredService<ICharacterRepository<ShapeLandCharacter>>(),
            services.GetRequiredService<ILogger<ShapeLandGame>>()));
        builder.Services.AddSingleton<IConnectionHandler>(services => services.GetRequiredService<ShapeLandGame>());
        builder.Services.AddSingleton(services => new TickLoop(ShapeLandRules.TickRate, services.GetRequiredService<ShapeLandGame>().Tick));
        builder.Services.AddHostedService<GameStartup>();

        var app = builder.Build();
        // Behind a proxy on this machine (Caddy, in the deployed game), the client's address comes from
        // X-Forwarded-For, so the per-address connection cap counts players, not the proxy. Only a loopback proxy
        // is trusted (ASP.NET's default), so no client can set the header for itself.
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
        var serverOptions = app.Services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value;
        if (!string.IsNullOrEmpty(serverOptions.WebRoot))
        {
            // The page's Content Security Policy, the same as shapeland/deploy/vercel.json gives the deployed game
            // (keep them in step), so a page change that breaks it shows up here first.
            var policy = PagePolicy(serverOptions.LoginUrl);
            app.Use(async (context, next) =>
            {
                context.Response.Headers.ContentSecurityPolicy = policy;
                await next();
            });

            // The page's settings, as the deploy writes them: where the Login server is.
            var config = $"window.shapelandLogin = {System.Text.Json.JsonSerializer.Serialize(serverOptions.LoginUrl)};\n";
            app.MapGet("/config.js", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-cache";
                return Results.Text(config, "text/javascript");
            });
        }

        ServeWebBuild(app, serverOptions.WebRoot);
        app.UseWebSockets();
        app.MapCometWebSocket("/ws");

        // Whether the server is up, for the web page on another host (it may be served from a CDN): any page may ask.
        app.MapGet("/health", (HttpContext context) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return "ok";
        });

        // The content's hash, so the editor can tell whether this server plays the content it has; for any page, like /health.
        var contentHash = app.Services.GetRequiredService<LoadedContent>().Hash;
        app.MapGet("/info", (HttpContext context) =>
        {
            context.Response.Headers.AccessControlAllowOrigin = "*";
            return Results.Json(new ServerInfo(contentHash));
        });

        app.Services.GetRequiredService<LoadedContent>(); // a missing content file stops the server now
        return app;
    }

    // Before the server takes connections (hosted services start before it listens): waits for the database
    // (the Login server migrates it, and makes the ticket keys before that), then builds the game and starts the
    // tick loop. On shutdown: stops the tick loop, saves everyone's spot and releases them, and flushes the saves.
    private sealed class GameStartup(IServiceProvider services, IHostApplicationLifetime lifetime, ILogger<GameStartup> logger) : IHostedService
    {
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await services.GetRequiredService<ICharacterRepository<ShapeLandCharacter>>().StartAsync(cancellationToken);
            var game = services.GetRequiredService<ShapeLandGame>(); // loads the content and builds the worlds
            logger.LogInformation("Playing {Zones}", string.Join(", ", game.Content.Zones.All.Select(z => z.Id)));

            var tickLoop = services.GetRequiredService<TickLoop>();
            tickLoop.Failed += (tick, e) =>
            {
                // Fail fast: the world may be half-updated, so stop rather than play on; the host flushes the log.
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

    // Serves a Unity web build at the root, for development. The game scene's build is Brotli-compressed with
    // the decompression fallback (files named .unityweb): browsers that accept br get Content-Encoding: br and
    // decompress natively; the rest (Firefox over plain HTTP) get the bytes as they are, and Unity's loader
    // unpacks them.
    // Scripts only from the page's own site (Unity's loader adds blob: scripts and needs 'wasm-unsafe-eval'), fonts
    // from Google Fonts, and connections to the page's own server and the Login server.
    private static string PagePolicy(string loginUrl)
    {
        var login = Uri.TryCreate(loginUrl, UriKind.Absolute, out var url) ? " " + url.GetLeftPart(UriPartial.Authority) : "";
        return "default-src 'self'; script-src 'self' blob: 'wasm-unsafe-eval'; style-src 'self' https://fonts.googleapis.com; "
            + "font-src https://fonts.gstatic.com; img-src 'self' data: blob:; worker-src 'self' blob:; "
            + $"connect-src 'self'{login}; object-src 'none'; base-uri 'none'";
    }

    private static void ServeWebBuild(WebApplication app, string webRoot)
    {
        if (string.IsNullOrEmpty(webRoot))
        {
            return;
        }

        if (!File.Exists(Path.Combine(webRoot, "index.html")))
        {
            throw new FileNotFoundException("No web build to serve (ShapeLand:WebRoot has no index.html).", webRoot);
        }

        var files = new PhysicalFileProvider(Path.GetFullPath(webRoot));
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".data"] = "application/octet-stream";
        types.Mappings[".bin"] = "application/octet-stream";
        types.Mappings[".unityweb"] = "application/octet-stream";
        types.Mappings[".webmanifest"] = "application/manifest+json";
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = types,
            OnPrepareResponse = context =>
            {
                if (context.File.Name == "content.bin")
                {
                    // Checked on every load, so a page reloaded for new content gets it.
                    context.Context.Response.Headers.CacheControl = "no-cache";
                    return;
                }

                if (!context.File.Name.EndsWith(".unityweb", StringComparison.Ordinal))
                {
                    return;
                }

                var headers = context.Context.Response.Headers;
                headers.Vary = "Accept-Encoding";
                if (context.Context.Request.Headers.AcceptEncoding.ToString().Contains("br", StringComparison.Ordinal))
                {
                    headers.ContentEncoding = "br";
                }
            },
        });
        app.Logger.LogInformation("Serving the web build in {WebRoot}", webRoot);
    }

    // /info's answer, written in camelCase ({"contentHash": "…"}).
    private sealed record ServerInfo(string ContentHash);

    // The content played, and its file's hash (Comet.Content.ContentHash).
    private sealed record LoadedContent(ShapeLandContent Content, string Hash);

    private static LoadedContent LoadContent(ShapeLandServerOptions options)
    {
        var path = options.ContentFile;
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(FindRepoRoot(), "artifacts", "content", "shapeland", "content.bin");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "No compiled content. Run the content build (dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build) or set ShapeLand:ContentFile.",
                path);
        }

        var file = File.ReadAllBytes(path);
        return new LoadedContent(ShapeLandContent.Load(new MemoryStream(file)), ContentHash.Of(file));
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root (ProjectComet.slnx); set ShapeLand:ContentFile.");
    }
}
