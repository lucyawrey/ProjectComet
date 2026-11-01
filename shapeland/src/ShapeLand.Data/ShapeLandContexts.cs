using Comet.Data;
using Comet.Data.Accounts;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ShapeLand.Data;

/// <summary>ShapeLand's account database: Comet's tables as they are.</summary>
public class ShapeLandAccountContext(DbContextOptions<ShapeLandAccountContext> options) : CometAccountContext(options);

/// <summary>ShapeLand's region database: Comet's tables, with the look on <c>character</c>.</summary>
public class ShapeLandRegionContext(DbContextOptions<ShapeLandRegionContext> options) : CometRegionContext(options)
{
    public DbSet<ShapeLandCharacter> ShapeLandCharacters => Set<ShapeLandCharacter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddCharacterColumns<ShapeLandCharacter>();
    }
}

// For dotnet ef (migrations), which needs a context without a running server. It never connects.
internal sealed class AccountContextFactory : IDesignTimeDbContextFactory<ShapeLandAccountContext>
{
    public ShapeLandAccountContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShapeLandAccountContext>();
        options.UseCometAccount("Host=localhost", TimeProvider.System);
        return new(options.Options);
    }
}

internal sealed class RegionContextFactory : IDesignTimeDbContextFactory<ShapeLandRegionContext>
{
    public ShapeLandRegionContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShapeLandRegionContext>();
        options.UseCometRegion("Host=localhost", TimeProvider.System);
        return new(options.Options);
    }
}
