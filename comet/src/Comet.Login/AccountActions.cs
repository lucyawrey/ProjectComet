using Comet.Data.Accounts;
using Comet.Data.Characters;
using Comet.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comet.Login;

/// <summary>
/// Bans and deletions kick an online player by adding one to their characters' owner versions, so the game server
/// drops them at its next save or version check without ever reading account data.
/// </summary>
public sealed class AccountActions<TCharacter>(
    IServiceScopeFactory scopes,
    RegionContexts<TCharacter> regions,
    ILoginRules<TCharacter> rules,
    IOptions<LoginOptions> options,
    TimeProvider clock,
    ILogger<AccountActions<TCharacter>> logger
)
    where TCharacter : Character
{
    // Just past a join ticket's lifetime: a ticket issued before the ban has expired by the second kick.
    private static readonly TimeSpan SecondKickDelay = TicketIssuer.Lifetime + TimeSpan.FromSeconds(1);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Kicks an online player now and again just after the ticket lifetime, catching a join with a ticket issued
    /// before the ban. The second kick is scheduled in memory.
    /// </summary>
    public async Task BanAsync(long accountId, CancellationToken cancellationToken)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CometAccountContext>();
            await db
                .Accounts.Where(a => a.Id == accountId)
                .ExecuteUpdateAsync(
                    set => set.SetProperty(a => a.Status, AccountStatus.Banned).SetProperty(a => a.UpdatedAt, Now),
                    cancellationToken
                );
        }

        await KickAsync(accountId, cancellationToken);
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(SecondKickDelay, clock);
                    await KickAsync(accountId, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "The second kick for banned account {Account} failed", accountId);
                }
            },
            CancellationToken.None
        );
    }

    /// <summary>
    /// The username stays held until the purge. With <paramref name="onlyGuest"/>, an account claimed meanwhile is left
    /// alone; returns whether it was deleted.
    /// </summary>
    public async Task<bool> DeleteAsync(long accountId, bool onlyGuest, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CometAccountContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = Now;
        var deleted = await db
            .Accounts.Where(a => a.Id == accountId && a.DeletedAt == null && (!onlyGuest || a.ClaimedAt == null))
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.DeletedAt, now).SetProperty(a => a.UpdatedAt, now), cancellationToken);
        if (deleted == 0)
        {
            return false;
        }

        await db.Sessions.Where(s => s.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        await using var region = await regions.JoinAsync(db, cancellationToken);
        if (rules.CharactersDeletedWithAccount)
        {
            await region
                .Characters.Where(c => c.AccountId == accountId && c.DeletedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.DeletedAt, now).SetProperty(c => c.UpdatedAt, now), cancellationToken);
        }

        await KickAsync(region, accountId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Runs in <paramref name="db"/>'s open transaction.
    /// </summary>
    public async Task RestoreAsync(CometAccountContext db, Account account, CancellationToken cancellationToken)
    {
        var deletedAt = account.DeletedAt;
        account.DeletedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        if (rules.CharactersDeletedWithAccount && deletedAt is { } since)
        {
            await using var region = await regions.JoinAsync(db, cancellationToken);
            var now = Now;
            await region
                .Characters.Where(c => c.AccountId == account.Id && c.DeletedAt >= since)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.DeletedAt, (DateTime?)null).SetProperty(c => c.UpdatedAt, now), cancellationToken);
        }
    }

    public async Task KickAsync(long accountId, CancellationToken cancellationToken)
    {
        await using var region = regions.Create();
        await KickAsync(region, accountId, cancellationToken);
    }

    /// <summary>
    /// Idle means no session use, status save or owner heartbeat for <see cref="LoginOptions.GuestIdle"/>.
    /// </summary>
    public async Task<int> CleanUpGuestsAsync(CancellationToken cancellationToken)
    {
        if (options.Value.GuestIdle is not { } idle)
        {
            return 0;
        }

        var cutoff = Now - idle;
        List<long> idleGuests;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CometAccountContext>();
            idleGuests = await db
                .Accounts.Where(a => a.ClaimedAt == null && a.DeletedAt == null && a.LastActiveAt < cutoff)
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);
        }

        if (idleGuests.Count == 0)
        {
            return 0;
        }

        await using (var region = regions.Create())
        {
            var active = await region
                .Characters.Where(c => idleGuests.Contains(c.AccountId))
                .Where(c =>
                    region.CharacterStatuses.Any(s => s.CharacterId == c.Id && s.UpdatedAt >= cutoff)
                    || region.CharacterOwners.Any(o => o.CharacterId == c.Id && o.UpdatedAt >= cutoff)
                )
                .Select(c => c.AccountId)
                .Distinct()
                .ToListAsync(cancellationToken);
            idleGuests = idleGuests.Except(active).ToList();
        }

        foreach (var accountId in idleGuests)
        {
            await DeleteAsync(accountId, onlyGuest: true, cancellationToken);
        }

        if (idleGuests.Count > 0)
        {
            logger.LogInformation("Deleted {Count} idle guest accounts", idleGuests.Count);
        }

        return idleGuests.Count;
    }

    /// <summary>
    /// Deleted guests go at once: a guest can't sign in to restore itself, so the hold would only keep its name taken.
    /// </summary>
    public async Task<(int Accounts, int Characters)> PurgeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CometAccountContext>();
        var guests = await db.Accounts.Where(a => a.ClaimedAt == null && a.DeletedAt != null).Select(a => a.Id).ToListAsync(cancellationToken);

        int characters;
        await using (var region = regions.Create())
        {
            var cutoff = Now - settings.CharacterHold;
            characters = await region
                .Characters.Where(c => c.DeletedAt < cutoff || (c.DeletedAt != null && guests.Contains(c.AccountId)))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var accountCutoff = Now - settings.AccountHold;
        var accounts = await db.Accounts.Where(a => a.DeletedAt < accountCutoff || guests.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);

        if (accounts + characters > 0)
        {
            logger.LogInformation("Purged {Accounts} accounts and {Characters} characters", accounts, characters);
        }

        return (accounts, characters);
    }

    private static Task<int> KickAsync(CometRegionContext<TCharacter> region, long accountId, CancellationToken cancellationToken) =>
        region
            .CharacterOwners.Where(o => region.Characters.Any(c => c.Id == o.CharacterId && c.AccountId == accountId))
            .ExecuteUpdateAsync(set => set.SetProperty(o => o.Version, o => o.Version + 1), cancellationToken);
}
