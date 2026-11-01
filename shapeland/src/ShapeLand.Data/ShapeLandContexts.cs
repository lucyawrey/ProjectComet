using Comet.Data;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShapeLand.Data;

/// <summary>ShapeLand's region database: Comet's tables, with the look on <c>character</c>.</summary>
public class ShapeLandRegionContext(DbContextOptions<ShapeLandRegionContext> options)
    : CometRegionContext<ShapeLandCharacter>(options);

// For dotnet ef (migrations) and the model checks in tests, which need a context without a server. It never connects.
public sealed class RegionContextFactory : IDesignTimeDbContextFactory<ShapeLandRegionContext>
{
    public ShapeLandRegionContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShapeLandRegionContext>();
        options.UseCometRegion("Host=localhost", TimeProvider.System);
        return new(options.Options);
    }
}
