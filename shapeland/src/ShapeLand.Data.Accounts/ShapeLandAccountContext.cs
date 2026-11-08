using Comet.Data.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShapeLand.Data.Accounts;

public class ShapeLandAccountContext(DbContextOptions<ShapeLandAccountContext> options) : CometAccountContext(options);

// For dotnet ef (migrations) and the model checks in tests, which need a context without a server. It never connects.
public sealed class AccountContextFactory : IDesignTimeDbContextFactory<ShapeLandAccountContext>
{
    public ShapeLandAccountContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShapeLandAccountContext>();
        options.UseCometAccount("Host=localhost", TimeProvider.System);
        return new(options.Options);
    }
}
