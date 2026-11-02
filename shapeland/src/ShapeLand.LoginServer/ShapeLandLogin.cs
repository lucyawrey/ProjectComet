using Comet.Login;
using Comet.Server;
using ShapeLand.Data;
using ShapeLand.Data.Accounts;
using ShapeLand.Shared.Content;

namespace ShapeLand.LoginServer;

/// <summary>Builds ShapeLand's Login server; used by Program and by tests, which run it in-process.</summary>
public static class ShapeLandLogin
{
    /// <summary>How long an unclaimed guest may sit idle before it's deleted, freeing the name.</summary>
    public static readonly TimeSpan GuestIdle = TimeSpan.FromDays(7);

    /// <summary>How long a deleted account (and its character) is held before the purge.</summary>
    public static readonly TimeSpan DeletedHold = TimeSpan.FromDays(7);

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        configure?.Invoke(builder);
        builder.AddCometLogFile();

        var shapeLand = builder.Configuration.GetSection("ShapeLand");
        var content = LoadContent(shapeLand["ContentFile"]);
        var gameServer = new GameServerAddress(
            shapeLand.GetValue("GameServer:Id", 1),
            shapeLand["GameServer:Address"] ?? throw new InvalidOperationException("Set ShapeLand:GameServer:Address."));
        builder.Services.AddSingleton(content);
        builder.Services.AddSingleton(gameServer);

        builder.AddCometLogin<ShapeLandAccountContext, ShapeLandRegionContext, ShapeLandCharacter, ShapeLandLoginRules>(login =>
        {
            login.GuestIdle = GuestIdle;
            login.AccountHold = DeletedHold;
            login.CharacterHold = DeletedHold;
            if (string.IsNullOrEmpty(login.ConnectionString))
            {
                throw new InvalidOperationException("Set Login:ConnectionString (PostgreSQL, as the database's owner).");
            }

            // Development: a key pair per checkout in artifacts/keys, made on first run, where the game server looks.
            if (string.IsNullOrEmpty(login.SigningKeyFile))
            {
                var keys = Path.Combine(RepoPaths.Root(), "artifacts", "keys");
                login.SigningKeyFile = Path.Combine(keys, "login.pem");
                login.PublicKeysFolder = Path.Combine(keys, "public");
                login.CreateSigningKey = true;
            }
        });

        var app = builder.Build();
        app.MapCometLogin<ShapeLandCharacter>();
        app.MapGet("/api/health", () => "ok");
        return app;
    }

    private static ShapeLandContent LoadContent(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            path = Path.Combine(RepoPaths.Root(), "artifacts", "content", "shapeland", "content.bin");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "No compiled content. Run the content build (dotnet run --project shapeland/src/ShapeLand.ContentBuild -- build) or set ShapeLand:ContentFile.",
                path);
        }

        using var file = File.OpenRead(path);
        return ShapeLandContent.Load(file);
    }
}

/// <summary>Where development defaults live: this repository's <c>artifacts/</c>.</summary>
internal static class RepoPaths
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root (ProjectComet.slnx); set the paths in the configuration.");
    }
}
