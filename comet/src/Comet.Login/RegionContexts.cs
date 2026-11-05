using Comet.Data;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Comet.Login;

/// <summary>Makes the game's region context, on its own connection or sharing the account context's.</summary>
public abstract class RegionContexts<TCharacter>(string connectionString, TimeProvider clock)
    where TCharacter : Character
{
    public CometRegionContext<TCharacter> Create() => Create(options => options.UseCometRegion(connectionString, clock));

    /// <summary>A region context in <paramref name="accounts"/>' open transaction, so one commit covers both schemas.</summary>
    public async Task<CometRegionContext<TCharacter>> JoinAsync(DbContext accounts, CancellationToken cancellationToken)
    {
        var transaction =
            accounts.Database.CurrentTransaction ?? throw new InvalidOperationException("Begin the account context's transaction first.");
        var region = Create(options => options.UseComet(accounts.Database.GetDbConnection(), CometRegion.Schema, clock));
        await region.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        return region;
    }

    protected abstract CometRegionContext<TCharacter> Create(Action<DbContextOptionsBuilder> configure);
}

internal sealed class RegionContexts<TCharacter, TRegion>(string connectionString, TimeProvider clock)
    : RegionContexts<TCharacter>(connectionString, clock)
    where TCharacter : Character
    where TRegion : CometRegionContext<TCharacter>
{
    protected override CometRegionContext<TCharacter> Create(Action<DbContextOptionsBuilder> configure)
    {
        var options = new DbContextOptionsBuilder<TRegion>();
        configure(options);
        return (TRegion)Activator.CreateInstance(typeof(TRegion), options.Options)!;
    }
}
