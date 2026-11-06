using Microsoft.EntityFrameworkCore;
using ShapeLand.Data.Accounts;

namespace ShapeLand.Data.Tests;

/// <summary>
/// Each context's model matches its last migration, so a model change without a migration (or a snapshot that loses
/// part of the model) fails here instead of in the next migration. No database needed.
/// </summary>
public class MigrationTests
{
    [Fact]
    public void AccountModelHasAMigration()
    {
        using var context = new AccountContextFactory().CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void RegionModelHasAMigration()
    {
        using var context = new RegionContextFactory().CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
