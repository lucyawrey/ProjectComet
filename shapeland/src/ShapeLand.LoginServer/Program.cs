using Comet.Server;
using ShapeLand.LoginServer;

try
{
    ShapeLandLogin.Build(args).Run();
}
catch (Exception e)
{
    FileLogging.LogFailure(args, e);
    throw;
}
