using StackBench.Metrics;

// Checks a stack benchmark run against the thresholds written down before it ran, and prints
// pass or fail. Exit code 0 when every check passes, 1 when any fails.
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: StackBench.Report <server.json> <bots.json> <thresholds.json> <profile>");
    Console.Error.WriteLine("  profile: a key in thresholds.json, e.g. clean or impaired");
    return 2;
}

var server = ResultFiles.Read<ServerResult>(args[0]);
var bots = ResultFiles.Read<BotsResult>(args[1]);
var profiles = ResultFiles.Read<Dictionary<string, Thresholds>>(args[2]);
var profile = args[3];
if (!profiles.TryGetValue(profile, out var limits))
{
    Console.Error.WriteLine($"No profile \"{profile}\" in {args[2]}.");
    return 2;
}

var checks = new List<(string Name, double Value, double? Limit)>
{
    ("Tick work, median (ms)", server.TickWork.MedianMs, limits.TickWorkMedianMs),
    ("Tick work, worst 1% (ms)", server.TickWork.P99Ms, limits.TickWorkP99Ms),
    ("Ticks over budget (fraction)", server.Ticks == 0 ? 1 : (double)server.TicksOverBudget / server.Ticks, limits.TicksOverBudgetFraction),
    ("Worst GC pause (ms)", server.GcPauses.MaxMs, limits.GcPauseMaxMs),
    ("Server CPU (cores)", server.CpuCores, limits.CpuCores),
    ("Gap between updates, worst 1% (ms)", bots.UpdateGap.P99Ms, limits.UpdateGapP99Ms),
    ("Round trip, median (ms)", bots.RoundTrip.MedianMs, limits.RoundTripMedianMs),
    ("Connection failures", bots.ConnectionFailures, limits.ConnectionFailures),
    ("Bytes down per bot per second", bots.BytesReceivedPerBotPerSecond, limits.BytesReceivedPerBotPerSecond),
};

Console.WriteLine($"Stack benchmark: {profile} run, {bots.Bots} bots, {server.TickRate} Hz, {server.WindowSeconds} s window");
Console.WriteLine();
Console.WriteLine($"{"Check",-38} {"Value",12} {"Limit",12}  Result");
var failed = 0;
foreach (var (name, value, limit) in checks)
{
    var result = limit is null ? "not checked" : value <= limit ? "PASS" : "FAIL";
    if (result == "FAIL")
    {
        failed++;
    }
    Console.WriteLine($"{name,-38} {value,12:0.###} {(limit is null ? "-" : limit.Value.ToString("0.###")),12}  {result}");
}

Console.WriteLine();
Console.WriteLine($"Also: server CPU {server.UserCpuCores} cores in its own code, {server.KernelCpuCores} in the kernel; "
    + $"tick lateness worst 1% {server.TickLateness.P99Ms} ms; {server.GcPauses.Count} GC pauses; "
    + $"{server.AllocatedBytesPerSecond / 1024:0} KB/s allocated; {server.FlushesSkipped} flushes skipped; "
    + $"{server.StateReplaced} state updates replaced; round trip worst 1% {bots.RoundTrip.P99Ms} ms.");

// The bots must have had CPU to spare, or their gaps and round trips measure themselves, not the server.
const double MaxBotsLoad = 0.8;
var botsLoad = bots.CpuCores / bots.ProcessorCount;
Console.WriteLine($"Bots CPU: {bots.CpuCores} of {bots.ProcessorCount} cores ({botsLoad:P0}).");
if (botsLoad > MaxBotsLoad)
{
    Console.WriteLine($"INVALID: the bots used over {MaxBotsLoad:P0} of their CPUs; give them more and rerun.");
    return 3;
}
Console.WriteLine(failed == 0 ? "PASSED" : $"FAILED ({failed} check{(failed == 1 ? "" : "s")})");
return failed == 0 ? 0 : 1;
