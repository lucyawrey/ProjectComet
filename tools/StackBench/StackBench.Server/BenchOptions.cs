namespace StackBench.Server;

/// <summary>Settings from the "Bench" section; override on the command line, e.g. <c>--Bench:DurationSeconds=60</c>.</summary>
public sealed class BenchOptions
{
    public int TickRate { get; set; } = 30;

    /// <summary>Entity updates sent to each connection per tick, chosen round-robin from all entities.</summary>
    public int UpdatesPerTick { get; set; } = 10;

    /// <summary>From the first connection to the start of the measurement window.</summary>
    public int WarmupSeconds { get; set; } = 60;

    public int DurationSeconds { get; set; } = 600;

    /// <summary>How long to keep serving after the window, so bots can finish theirs.</summary>
    public int ShutdownGraceSeconds { get; set; } = 30;

    /// <summary>A <c>GCLatencyMode</c> name to set when the server has started, e.g. SustainedLowLatency; empty keeps the default.</summary>
    public string? GcLatencyMode { get; set; }

    public string ResultsPath { get; set; } = "tools/StackBench/results/server.json";
}
