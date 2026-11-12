using Microsoft.EntityFrameworkCore;
using ShapeLand.Data.Accounts;

namespace ShapeLand.Data.Tests;

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
