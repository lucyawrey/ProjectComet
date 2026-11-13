using Comet.Data;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShapeLand.Data;

public class ShapeLandRegionContext(DbContextOptions<ShapeLandRegionContext> options) : CometRegionContext<ShapeLandCharacter>(options);

public sealed class RegionContextFactory : IDesignTimeDbContextFactory<ShapeLandRegionContext>
{
    public ShapeLandRegionContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShapeLandRegionContext>();
        options.UseCometRegion("Host=localhost", TimeProvider.System);
        return new(options.Options);
    }
}
