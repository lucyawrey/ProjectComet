using Comet.Server;
using ShapeLand.GameServer;

// ShapeLand's phase 0 game server: one zone, players connect straight to it with a name.
try
{
    ShapeLandServer.Build(args).Run();
}
catch (Exception e)
{
    FileLogging.LogFailure(args, e);
    throw;
}
