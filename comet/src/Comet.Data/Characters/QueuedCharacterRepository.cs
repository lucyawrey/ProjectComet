using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Comet.Data.Characters;

/// <summary>A queued status save: the character, the owner version it was made under, and where it is.</summary>
public readonly record struct StatusSave(long CharacterId, long Version, CharacterPosition Position);

/// <summary>A character this server believes it holds, under <see cref="Version"/>.</summary>
public readonly record struct HeldCharacter(long CharacterId, long Version);

/// <summary>
/// The save queue behind every <see cref="ICharacterRepository{TCharacter}"/>: one writer task runs claims, look
/// saves, leaves and each interval's batch in the order they came, retrying with backoff while the database is
/// unreachable. A backend (PostgreSQL, or in memory for tests) supplies the single operations.
/// </summary>
public abstract class QueuedCharacterRepository<TCharacter> : ICharacterRepository<TCharacter>
    where TCharacter : Character
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<Operation> _operations = Channel.CreateUnbounded<Operation>(new UnboundedChannelOptions { SingleReader = true });

    // Status saves waiting for the next interval, latest per character. Written by the game's thread, taken by the
    // writer.
    private readonly Lock _pendingLock = new();
    private Dictionary<long, StatusSave> _pending = [];

    // What this server holds, by character. The writer's alone.
    private readonly Dictionary<long, long> _held = [];

    private readonly CancellationTokenSource _stopTimer = new();
    private readonly CancellationTokenSource _abort = new();
    private Task? _writer;
    private Task? _timer;
    private bool _stopped;

    protected QueuedCharacterRepository(CharacterRepositoryOptions options, TimeProvider clock, ILogger logger)
    {
        Options = options;
        Clock = clock;
        Logger = logger;
    }

    protected CharacterRepositoryOptions Options { get; }
    protected TimeProvider Clock { get; }
    protected ILogger Logger { get; }

    public event Action<SaveRefused>? SaveRefused;

    // Backend operations. Each runs on the writer's thread, one at a time, and may throw; IsTransient decides
    // whether the writer retries.

    /// <summary>Waits until the database is ready, then releases every character held under this server's ID.</summary>
    protected abstract Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Claims and loads a character (<see cref="ClaimOutcome.Claimed"/> or <see cref="ClaimOutcome.NotFound"/>).</summary>
    protected abstract Task<CharacterClaim<TCharacter>> ClaimCoreAsync(long characterId, CancellationToken cancellationToken);

    /// <summary>Writes the saves whose version still holds, returning the characters whose saves applied.</summary>
    protected abstract Task<IReadOnlySet<long>> SaveStatusesCoreAsync(IReadOnlyList<StatusSave> saves, CancellationToken cancellationToken);

    /// <summary>Writes the game's own columns if the version still holds; false if it doesn't.</summary>
    protected abstract Task<bool> SaveCharacterCoreAsync(TCharacter character, long version, CancellationToken cancellationToken);

    /// <summary>Writes the final status (if any) and releases the character, if the version still holds.</summary>
    protected abstract Task LeaveCoreAsync(long characterId, long version, CharacterPosition? position, CancellationToken cancellationToken);

    /// <summary>Returns the characters still held under their versions, and marks them as active.</summary>
    protected abstract Task<IReadOnlySet<long>> CheckHeldCoreAsync(IReadOnlyList<HeldCharacter> held, CancellationToken cancellationToken);

    /// <summary>Whether a failure is the database being unreachable or busy, so the writer retries it.</summary>
    protected virtual bool IsTransient(Exception exception) => false;

    public abstract Task<string?> LoadNameAsync(long characterId, CancellationToken cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_writer != null)
        {
            throw new InvalidOperationException("The repository has already started.");
        }

        await PrepareAsync(cancellationToken);
        _writer = Task.Run(WriteAsync, CancellationToken.None);
        _timer = Task.Run(TickAsync, CancellationToken.None);
    }

    public async Task<CharacterClaim<TCharacter>> ClaimAsync(long characterId, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<CharacterClaim<TCharacter>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_writer == null || !_operations.Writer.TryWrite(new ClaimOperation(characterId, done, cancellationToken)))
        {
            return new(ClaimOutcome.Unavailable);
        }

        await using var registration = cancellationToken.Register(() => done.TrySetResult(new(ClaimOutcome.Unavailable)));
        return await done.Task;
    }

    public void SaveStatus(long characterId, long version, CharacterPosition position)
    {
        lock (_pendingLock)
        {
            // An older connection's save never replaces a newer one's.
            if (!_pending.TryGetValue(characterId, out var queued) || queued.Version <= version)
            {
                _pending[characterId] = new(characterId, version, position);
            }
        }
    }

    public void SaveCharacter(TCharacter character, long version) =>
        _operations.Writer.TryWrite(new LookOperation((TCharacter)character.Copy(), version));

    public Task LeaveAsync(long characterId, long version, CharacterPosition? position)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operations.Writer.TryWrite(new LeaveOperation(characterId, version, position, done)))
        {
            Logger.LogWarning("Character {Character} left after the repository stopped; its final save is lost", characterId);
            done.SetResult();
        }

        return done.Task;
    }

    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operations.Writer.TryWrite(new FlushOperation(done)))
        {
            done.SetResult();
        }

        return done.Task;
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (_writer == null || _stopped)
        {
            return;
        }

        _stopped = true;
        await _stopTimer.CancelAsync();
        _operations.Writer.TryWrite(new FlushOperation(null));
        _operations.Writer.Complete();
        _abort.CancelAfter(timeout);
        await _writer;
        await _timer!;

        int lost;
        lock (_pendingLock)
        {
            lost = _pending.Count;
        }

        if (lost > 0)
        {
            Logger.LogWarning("Stopped with {Count} status saves unwritten", lost);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(DefaultStopTimeout);
        _stopTimer.Dispose();
        _abort.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(Options.SaveInterval, Clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_stopTimer.Token))
            {
                _operations.Writer.TryWrite(new FlushOperation(null));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task WriteAsync()
    {
        await foreach (var operation in _operations.Reader.ReadAllAsync())
        {
            try
            {
                await (
                    operation switch
                    {
                        ClaimOperation claim => ClaimNowAsync(claim),
                        LookOperation look => SaveLookNowAsync(look),
                        LeaveOperation leave => LeaveNowAsync(leave),
                        FlushOperation flush => FlushNowAsync(flush),
                        _ => throw new InvalidOperationException($"Unknown operation {operation}"),
                    }
                );
            }
            catch (Exception exception)
            {
                // A bug, or the stop deadline passing: never let one operation stop the writer.
                Logger.LogError(exception, "A character save failed and was dropped: {Operation}", operation);
                operation.Fail();
            }
        }
    }

    private async Task ClaimNowAsync(ClaimOperation claim)
    {
        if (claim.CancellationToken.IsCancellationRequested)
        {
            return; // the joiner gave up while it waited in the queue
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(claim.CancellationToken, _abort.Token);

        // The character's own queued status goes first, so the claim loads what's newest.
        StatusSave? queued = null;
        lock (_pendingLock)
        {
            if (_pending.Remove(claim.CharacterId, out var save))
            {
                queued = save;
            }
        }

        CharacterClaim<TCharacter> result;
        try
        {
            if (queued is { } save)
            {
                await RetryAsync(
                    async token =>
                    {
                        await SaveStatusesAsync([save], token);
                        return true;
                    },
                    cancel.Token
                );
            }

            result = await RetryAsync(token => ClaimCoreAsync(claim.CharacterId, token), cancel.Token);
        }
        catch (OperationCanceledException) when (claim.CancellationToken.IsCancellationRequested)
        {
            return; // the joiner has its Unavailable already
        }

        if (result.Outcome != ClaimOutcome.Claimed)
        {
            claim.Done.TrySetResult(result);
            return;
        }

        _held[claim.CharacterId] = result.Version;
        if (!claim.Done.TrySetResult(result))
        {
            // The joiner gave up while the claim ran: let the character go again.
            await LeaveNowAsync(new LeaveOperation(claim.CharacterId, result.Version, null, new()));
        }
    }

    private async Task SaveLookNowAsync(LookOperation look)
    {
        var applied = await RetryAsync(token => SaveCharacterCoreAsync(look.Character, look.Version, token), _abort.Token);
        if (!applied)
        {
            Refuse(look.Character.Id, look.Version);
        }
    }

    private async Task LeaveNowAsync(LeaveOperation leave)
    {
        var position = leave.Position;
        lock (_pendingLock)
        {
            // The final save replaces a queued one from the same connection, or carries it if there's none.
            if (_pending.TryGetValue(leave.CharacterId, out var queued) && queued.Version <= leave.Version)
            {
                _pending.Remove(leave.CharacterId);
                if (queued.Version == leave.Version)
                {
                    position ??= queued.Position;
                }
            }
        }

        await RetryAsync(
            async token =>
            {
                await LeaveCoreAsync(leave.CharacterId, leave.Version, position, token);
                return true;
            },
            _abort.Token
        );

        if (_held.TryGetValue(leave.CharacterId, out var version) && version == leave.Version)
        {
            _held.Remove(leave.CharacterId);
        }

        leave.Done.TrySetResult();
    }

    private async Task FlushNowAsync(FlushOperation flush)
    {
        Dictionary<long, StatusSave> batch;
        lock (_pendingLock)
        {
            batch = _pending;
            _pending = [];
        }

        try
        {
            if (batch.Count > 0)
            {
                await SaveStatusesAsync([.. batch.Values], _abort.Token);
            }

            if (_held.Count > 0)
            {
                var held = _held.Select(pair => new HeldCharacter(pair.Key, pair.Value)).ToList();
                var still = await CheckHeldCoreAsync(held, _abort.Token);
                foreach (var character in held.Where(character => !still.Contains(character.CharacterId)))
                {
                    _held.Remove(character.CharacterId);
                    Refuse(character.CharacterId, character.Version);
                }
            }
        }
        catch (Exception exception) when (IsTransient(exception) && !_abort.IsCancellationRequested)
        {
            // Unreachable: keep the batch for the next interval, under any newer saves made meanwhile. Players play on.
            Logger.LogWarning(exception, "Couldn't write {Count} status saves; retrying next interval", batch.Count);
            lock (_pendingLock)
            {
                foreach (var save in batch.Values)
                {
                    _pending.TryAdd(save.CharacterId, save);
                }
            }
        }

        flush.Done?.TrySetResult();
    }

    private async Task SaveStatusesAsync(IReadOnlyList<StatusSave> saves, CancellationToken cancellationToken)
    {
        var applied = await SaveStatusesCoreAsync(saves, cancellationToken);
        foreach (var save in saves.Where(save => !applied.Contains(save.CharacterId)))
        {
            if (_held.TryGetValue(save.CharacterId, out var version) && version == save.Version)
            {
                _held.Remove(save.CharacterId);
            }

            Refuse(save.CharacterId, save.Version);
        }
    }

    private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;
        while (true)
        {
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning(exception, "The character database is unreachable; retrying in {Delay}", delay);
                await Task.Delay(delay, Clock, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
            }
        }
    }

    private void Refuse(long characterId, long version)
    {
        try
        {
            SaveRefused?.Invoke(new(characterId, version));
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "A SaveRefused handler failed");
        }
    }

    private abstract record Operation
    {
        public virtual void Fail() { }
    }

    private sealed record ClaimOperation(long CharacterId, TaskCompletionSource<CharacterClaim<TCharacter>> Done, CancellationToken CancellationToken)
        : Operation
    {
        public override void Fail() => Done.TrySetResult(new(ClaimOutcome.Unavailable));
    }

    private sealed record LookOperation(TCharacter Character, long Version) : Operation;

    private sealed record LeaveOperation(long CharacterId, long Version, CharacterPosition? Position, TaskCompletionSource Done) : Operation
    {
        public override void Fail() => Done.TrySetResult();
    }

    private sealed record FlushOperation(TaskCompletionSource? Done) : Operation
    {
        public override void Fail() => Done?.TrySetResult();
    }
}
