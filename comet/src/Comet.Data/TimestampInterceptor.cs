using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Comet.Data;

/// <summary>
/// Sets <see cref="ITimestamped.CreatedAt"/> and <see cref="ITimestamped.UpdatedAt"/> on added rows, and
/// <see cref="ITimestamped.UpdatedAt"/> on changed ones, as they're saved. Bulk updates (<c>ExecuteUpdate</c>) skip
/// it and set <c>UpdatedAt</c> themselves.
/// </summary>
public sealed class TimestampInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in context.ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
