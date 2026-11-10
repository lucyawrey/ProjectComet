using Comet.Login;
using Comet.Server;
using ShapeLand.Data;
using ShapeLand.Data.Accounts;
using ShapeLand.Shared;
using ShapeLand.Shared.Content;

namespace ShapeLand.LoginServer;

public static class ShapeLandLogin
{
    private const string Hint = "; set the paths in the configuration.";

    public static readonly TimeSpan GuestIdle = TimeSpan.FromDays(7);

    public static readonly TimeSpan DeletedHold = TimeSpan.FromDays(7);

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        configure?.Invoke(builder);
        builder.AddCometLogFile();

        var shapeLand = builder.Configuration.GetSection("ShapeLand");
        var content = LoadContent(shapeLand["ContentFile"]);
        var gameServer = new GameServerAddress(
            shapeLand.GetValue("GameServer:Id", 1),
            shapeLand["GameServer:Address"] ?? throw new InvalidOperationException("Set ShapeLand:GameServer:Address.")
        );
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

            if (string.IsNullOrEmpty(login.SigningKeyFile))
            {
                var keys = Path.Combine(RepoPaths.Root(Hint), "artifacts", "keys");
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
        using var file = File.OpenRead(RepoPaths.ContentFile(path, Hint));
        return ShapeLandContent.Load(file);
    }
}
