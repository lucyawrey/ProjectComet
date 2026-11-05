using Comet.Data.Accounts;
using Comet.Data.Characters;
using Comet.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comet.Login;

/// <summary>
/// Before the server takes requests (hosted services start before it listens): loads the signing key, so a missing
/// one stops startup, and applies both schemas' migrations. Only the Login server migrates; game servers wait for it.
/// </summary>
internal sealed class Startup<TCharacter>(
    IServiceScopeFactory scopes,
    RegionContexts<TCharacter> regions,
    IOptions<LoginOptions> options,
    ILogger<Startup<TCharacter>> logger
) : IHostedService
    where TCharacter : Character
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var keys = scopes.CreateAsyncScope())
        {
            keys.ServiceProvider.GetRequiredService<TicketIssuer>();
        }

        if (!options.Value.Migrate)
        {
            return;
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CometAccountContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        await using (var region = regions.Create())
        {
            await region.Database.MigrateAsync(cancellationToken);
        }

        logger.LogInformation("The account and region schemas are up to date");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Runs the guest cleanup and the purge every <see cref="LoginOptions.CleanupPeriod"/>, starting soon after startup.</summary>
internal sealed class Cleanup<TCharacter>(
    AccountActions<TCharacter> actions,
    IOptions<LoginOptions> options,
    TimeProvider clock,
    ILogger<Cleanup<TCharacter>> logger
) : BackgroundService
    where TCharacter : Character
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupPeriod, clock);
        do
        {
            try
            {
                await actions.CleanUpGuestsAsync(stoppingToken);
                await actions.PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The account cleanup failed; it runs again next period");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
