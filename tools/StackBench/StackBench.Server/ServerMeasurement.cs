using System.Diagnostics;
using Comet.Server.Connections;
using Comet.Server.Diagnostics;
using Comet.Server.Ticking;
using Microsoft.Extensions.Options;
using StackBench.Metrics;

namespace StackBench.Server;

/// <summary>
/// Measures the window that starts a warmup after the first connection and lasts the configured
/// duration, then writes the results and shuts the server down after a grace period.
/// Tick timings are recorded on the tick thread; window start and end also happen there.
/// </summary>
public sealed class ServerMeasurement : IDisposable
{
    private readonly BenchOptions _options;
    private readonly ConnectionRegistry _registry;
    private readonly TickLoop _tickLoop;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ServerMeasurement> _logger;
    private readonly GcPauseListener _gcListener = new();
    private readonly Lock _gcLock = new();

    private readonly LatencyRecorder _tickWork = new();
    private readonly LatencyRecorder _tickLateness = new();
    private readonly LatencyRecorder _gcPauses = new();

    private long _windowStart;
    private long _windowEnd;
    private volatile bool _measuring;
    private bool _done;
    private long _ticks;
    private long _ticksOverBudget;
    private double _connectionSum;
    private Snapshot _startSnapshot;

    public ServerMeasurement(
        IOptions<BenchOptions> options,
        ConnectionRegistry registry,
        TickLoop tickLoop,
        BenchGame game,
        IHostApplicationLifetime lifetime,
        ILogger<ServerMeasurement> logger)
    {
        _options = options.Value;
        _registry = registry;
        _tickLoop = tickLoop;
        _lifetime = lifetime;
        _logger = logger;

        game.FirstConnection += OnFirstConnection;
        tickLoop.TickCompleted += OnTickCompleted;
        _gcListener.PauseRecorded += OnGcPause;
    }

    public void Dispose() => _gcListener.Dispose();

    private void OnFirstConnection()
    {
        var now = Stopwatch.GetTimestamp();
        var windowStart = now + (long)(_options.WarmupSeconds * (double)Stopwatch.Frequency);
        Volatile.Write(ref _windowEnd, windowStart + (long)(_options.DurationSeconds * (double)Stopwatch.Frequency));
        Volatile.Write(ref _windowStart, windowStart);
        _logger.LogInformation("First connection; measuring for {Duration} s after a {Warmup} s warmup",
            _options.DurationSeconds, _options.WarmupSeconds);
    }

    private void OnGcPause(TimeSpan pause)
    {
        if (_measuring)
        {
            lock (_gcLock)
            {
                _gcPauses.Record(pause);
            }
        }
    }

    private void OnTickCompleted(TickTiming timing)
    {
        var windowStart = Volatile.Read(ref _windowStart);
        if (_done || windowStart == 0)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (!_measuring)
        {
            if (now >= windowStart)
            {
                _startSnapshot = Snapshot.Take(_registry);
                _measuring = true;
                _logger.LogInformation("Measurement window started");
            }
            return;
        }

        _ticks++;
        _tickWork.Record(timing.Work);
        _tickLateness.Record(timing.Lateness);
        if (timing.Work > _tickLoop.Period)
        {
            _ticksOverBudget++;
        }
        _connectionSum += _registry.Snapshot.Length;

        if (now >= Volatile.Read(ref _windowEnd))
        {
            _measuring = false;
            _done = true;
            var result = BuildResult(Snapshot.Take(_registry));
            _ = Task.Run(() => FinishAsync(result));
        }
    }

    private ServerResult BuildResult(Snapshot end)
    {
        var start = _startSnapshot;
        var seconds = Stopwatch.GetElapsedTime(start.Timestamp, end.Timestamp).TotalSeconds;
        var averageConnections = _ticks == 0 ? 0 : _connectionSum / _ticks;
        var perConnectionSecond = Math.Max(averageConnections, 1) * seconds;
        LatencySummary gcPauses;
        lock (_gcLock)
        {
            gcPauses = _gcPauses.Summarize();
        }

        return new ServerResult
        {
            TickRate = _tickLoop.TickRate,
            WindowSeconds = Math.Round(seconds, 1),
            Ticks = _ticks,
            TickWork = _tickWork.Summarize(),
            TickLateness = _tickLateness.Summarize(),
            TicksOverBudget = _ticksOverBudget,
            GcPauses = gcPauses,
            Gen0Collections = end.Gen0 - start.Gen0,
            Gen1Collections = end.Gen1 - start.Gen1,
            Gen2Collections = end.Gen2 - start.Gen2,
            AllocatedBytesPerSecond = Math.Round((end.Allocated - start.Allocated) / seconds),
            CpuCores = Math.Round((end.Cpu - start.Cpu).TotalSeconds / seconds, 3),
            WorkingSetMegabytes = Math.Round(end.WorkingSet / 1_048_576.0, 1),
            AverageConnections = Math.Round(averageConnections, 1),
            BytesSentPerConnectionPerSecond = Math.Round((end.BytesSent - start.BytesSent) / perConnectionSecond),
            BytesReceivedPerConnectionPerSecond = Math.Round((end.BytesReceived - start.BytesReceived) / perConnectionSecond),
            FlushesSkipped = end.FlushesSkipped - start.FlushesSkipped,
            StateReplaced = end.StateReplaced - start.StateReplaced,
            ProtocolErrors = end.ProtocolErrors - start.ProtocolErrors,
        };
    }

    private async Task FinishAsync(ServerResult result)
    {
        var path = ResultFiles.Write(_options.ResultsPath, result);
        _logger.LogInformation("Measurement window ended; results written to {Path}", path);

        var grace = Stopwatch.StartNew();
        while (_registry.Snapshot.Length > 0 && grace.Elapsed < TimeSpan.FromSeconds(_options.ShutdownGraceSeconds))
        {
            await Task.Delay(250);
        }
        _lifetime.StopApplication();
    }

    private readonly record struct Snapshot(
        long Timestamp,
        int Gen0,
        int Gen1,
        int Gen2,
        long Allocated,
        TimeSpan Cpu,
        long WorkingSet,
        long BytesSent,
        long BytesReceived,
        long FlushesSkipped,
        long StateReplaced,
        long ProtocolErrors)
    {
        public static Snapshot Take(ConnectionRegistry registry)
        {
            using var process = Process.GetCurrentProcess();
            var stats = registry.Stats;
            return new Snapshot(
                Stopwatch.GetTimestamp(),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2),
                GC.GetTotalAllocatedBytes(),
                process.TotalProcessorTime,
                process.WorkingSet64,
                stats.BytesSent,
                stats.BytesReceived,
                stats.FlushesSkipped,
                stats.StateReplaced,
                stats.ProtocolErrors);
        }
    }
}
