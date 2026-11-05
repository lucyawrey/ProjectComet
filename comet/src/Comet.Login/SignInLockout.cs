using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Comet.Login;

/// <summary>
/// Counts wrong passwords per account and address together, so a stranger elsewhere can't lock someone out by
/// knowing their name. After <see cref="LoginOptions.LockoutFailures"/> within <see cref="LoginOptions.LockoutDuration"/>,
/// that pair is refused until the duration passes. In memory: a restart resets it. Recovery codes ignore it.
/// </summary>
public sealed class SignInLockout(IOptions<LoginOptions> options, TimeProvider clock)
{
    private const int PruneAbove = 10_000;

    private readonly ConcurrentDictionary<(long Account, string Address), Entry> _entries = new();

    /// <summary>Whether the pair is locked out now.</summary>
    public bool IsLocked(long accountId, string address) => _entries.TryGetValue((accountId, address), out var entry) && entry.LockedUntil > Now;

    public void Failed(long accountId, string address)
    {
        var settings = options.Value;
        var now = Now;
        _entries.AddOrUpdate(
            (accountId, address),
            _ => Next(new Entry(0, now, DateTime.MinValue), now, settings),
            (_, entry) => Next(entry, now, settings)
        );

        if (_entries.Count > PruneAbove)
        {
            foreach (var (key, entry) in _entries)
            {
                if (entry.LockedUntil <= now && now - entry.Since > settings.LockoutDuration)
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }
    }

    public void Succeeded(long accountId, string address) => _entries.TryRemove((accountId, address), out _);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static Entry Next(Entry entry, DateTime now, LoginOptions settings)
    {
        // Failures count within one window; a lockout that ended starts a fresh one.
        if (now - entry.Since > settings.LockoutDuration || (entry.LockedUntil != DateTime.MinValue && entry.LockedUntil <= now))
        {
            entry = new Entry(0, now, DateTime.MinValue);
        }

        var failures = entry.Failures + 1;
        return failures >= settings.LockoutFailures ? new Entry(0, now, now + settings.LockoutDuration) : entry with { Failures = failures };
    }

    private sealed record Entry(int Failures, DateTime Since, DateTime LockedUntil);
}
