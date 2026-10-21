using Comet.Content;
using Comet.Server;
using Comet.Server.Connections;
using Comet.Server.Ticking;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
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
        builder.Services.AddSingleton(services => new ShapeLandGame(
            services.GetRequiredService<ShapeLandContent>(),
            services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.StartSpawn,
            services.GetRequiredService<ConnectionRegistry>(),
            services.GetRequiredService<ILogger<ShapeLandGame>>()));
        builder.Services.AddSingleton<IConnectionHandler>(services => services.GetRequiredService<ShapeLandGame>());
        builder.Services.AddSingleton(services => new TickLoop(ShapeLandRules.TickRate, services.GetRequiredService<ShapeLandGame>().Tick));

        var app = builder.Build();
        // Behind a proxy on this machine (Caddy, in the public test build), the client's address comes from
        // X-Forwarded-For, so the per-address connection cap counts players, not the proxy. Only a loopback proxy
        // is trusted (ASP.NET's default), so no client can set the header for itself.
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
        ServeWebBuild(app, app.Services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.WebRoot);
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

        var tickLoop = app.Services.GetRequiredService<TickLoop>();
        var game = app.Services.GetRequiredService<ShapeLandGame>(); // load content and build the worlds before accepting players
        app.Logger.LogInformation("Playing {Zones}", string.Join(", ", game.Content.Zones.All.Select(z => z.Id)));
        tickLoop.Failed += (tick, e) =>
        {
            // Fail fast: the world may be half-updated, so stop rather than play on; the host flushes the log.
            app.Logger.LogCritical(e, "The game failed on tick {Tick}; stopping the server", tick);
            Environment.ExitCode = 1;
            app.Lifetime.StopApplication();
        };
        app.Lifetime.ApplicationStarted.Register(tickLoop.Start);
        app.Lifetime.ApplicationStopping.Register(tickLoop.Stop);
        return app;
    }

    // Serves a Unity web build at the root, for development. The game scene's build is Brotli-compressed with
    // the decompression fallback (files named .unityweb): browsers that accept br get Content-Encoding: br and
    // decompress natively; the rest (Firefox over plain HTTP) get the bytes as they are, and Unity's loader
    // unpacks them.
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
