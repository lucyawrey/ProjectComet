using Comet.Server;
using ShapeLand.GameServer;

try
{
    ShapeLandServer.Build(args).Run();
}
catch (Exception e)
{
    FileLogging.LogFailure(args, e);
    throw;
}
