using Comet.Server;
using Comet.Server.Connections;
using Comet.Server.Ticking;
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
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        // Settings load from the build output, so the server can be started from any folder.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder);

        builder.Services.Configure<ShapeLandServerOptions>(builder.Configuration.GetSection("ShapeLand"));
        builder.Services.AddSingleton(services => LoadContent(services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value));
        builder.Services.AddSingleton(new ConnectionRegistry(ShapeLandProtocol.Options));
        builder.Services.AddSingleton<ShapeLandGame>();
        builder.Services.AddSingleton<IConnectionHandler>(services => services.GetRequiredService<ShapeLandGame>());
        builder.Services.AddSingleton(services => new TickLoop(ShapeLandRules.TickRate, services.GetRequiredService<ShapeLandGame>().Tick));

        var app = builder.Build();
        ServeWebBuild(app, app.Services.GetRequiredService<IOptions<ShapeLandServerOptions>>().Value.WebRoot);
        app.UseWebSockets();
        app.MapCometWebSocket("/ws");

        var tickLoop = app.Services.GetRequiredService<TickLoop>();
        app.Services.GetRequiredService<ShapeLandGame>(); // load content and build the world before accepting players
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
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = types,
            OnPrepareResponse = context =>
            {
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

    private static ShapeLandContent LoadContent(ShapeLandServerOptions options)
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

        using var stream = File.OpenRead(path);
        return ShapeLandContent.Load(stream);
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
