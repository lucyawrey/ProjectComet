using Comet.Server;
using ShapeLand.LoginServer;

// ShapeLand's Login server: accounts, sessions and join tickets, on Comet.Login.
try
{
    ShapeLandLogin.Build(args).Run();
}
catch (Exception e)
{
    FileLogging.LogFailure(args, e);
    throw;
}
