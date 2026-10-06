using System.Text.Json;
using System.Text.Json.Serialization;

namespace StackBench.Metrics;

/// <summary>What the bench server measured during the measurement window.</summary>
public sealed record ServerResult
{
    public required int TickRate { get; init; }
    public required double WindowSeconds { get; init; }
    public required long Ticks { get; init; }

    /// <summary>Time spent on each tick's work.</summary>
    public required LatencySummary TickWork { get; init; }

    /// <summary>How late each tick started against its schedule.</summary>
    public required LatencySummary TickLateness { get; init; }

    /// <summary>Ticks whose work took longer than the tick period.</summary>
    public required long TicksOverBudget { get; init; }

    public required LatencySummary GcPauses { get; init; }
    public required int Gen0Collections { get; init; }
    public required int Gen1Collections { get; init; }
    public required int Gen2Collections { get; init; }
    public required double AllocatedBytesPerSecond { get; init; }

    /// <summary>CPU time used, as a fraction of one core.</summary>
    public required double CpuCores { get; init; }

    /// <summary>The part of <see cref="CpuCores"/> spent in the process's own code.</summary>
    public required double UserCpuCores { get; init; }

    /// <summary>The part of <see cref="CpuCores"/> spent in the kernel on the process's behalf (system calls, network stack).</summary>
    public required double KernelCpuCores { get; init; }

    public required double WorkingSetMegabytes { get; init; }
    public required double AverageConnections { get; init; }
    public required double BytesSentPerConnectionPerSecond { get; init; }
    public required double BytesReceivedPerConnectionPerSecond { get; init; }
    public required long FlushesSkipped { get; init; }
    public required long StateReplaced { get; init; }
    public required long ProtocolErrors { get; init; }
}

/// <summary>What the bots measured during the measurement window, all bots merged.</summary>
public sealed record BotsResult
{
    public required int Bots { get; init; }
    public required double WindowSeconds { get; init; }
    public required LatencySummary RoundTrip { get; init; }

    /// <summary>Time between frames carrying entity updates.</summary>
    public required LatencySummary UpdateGap { get; init; }

    public required double BytesReceivedPerBotPerSecond { get; init; }
    public required double BytesSentPerBotPerSecond { get; init; }

    /// <summary>Bots that failed to connect, or lost their connection before the window ended.</summary>
    public required int ConnectionFailures { get; init; }
}

/// <summary>Pass/fail limits for one run profile: each value must be at most its limit. A null limit isn't checked.</summary>
public sealed record Thresholds
{
    public double? TickWorkMedianMs { get; init; }
    public double? TickWorkP99Ms { get; init; }
    public double? TicksOverBudgetFraction { get; init; }
    public double? GcPauseMaxMs { get; init; }
    public double? CpuCores { get; init; }
    public double? UpdateGapP99Ms { get; init; }
    public double? RoundTripMedianMs { get; init; }
    public int? ConnectionFailures { get; init; }
    public double? BytesReceivedPerBotPerSecond { get; init; }
}

public static class ResultFiles
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes a results file and returns its full path.</summary>
    public static string Write<T>(string path, T result)
    {
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(result, Options));
        return fullPath;
    }

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Resolve(path)), Options)
        ?? throw new InvalidDataException($"{path} is empty.");

    /// <summary>
    /// Relative paths are relative to the repository root (the folder holding ProjectComet.slnx,
    /// searched upwards from the current folder), because <c>dotnet run</c> starts web projects in
    /// their own folder and console projects in the current one. Outside the repository (e.g. in a
    /// container) they're relative to the current folder.
    /// </summary>
    public static string Resolve(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
            {
                return Path.Combine(dir.FullName, path);
            }
        }
        return Path.GetFullPath(path);
    }
}
