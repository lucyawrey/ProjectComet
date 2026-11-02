// ShapeLand's simulated network profiles, for tools/run shapeland-dev --net and tools/run shapeland-netrun. Each is
// netem's delay and jitter each way plus loss each way, on the game server's link (shapeland/docker).
//
//   none   nothing added
//   good   40 ms ± 10 ms and 1% loss each way (about 80 ms round trip)
//   bad    100 ms ± 25 ms and 2% loss each way (about 200 ms round trip)
//   awful  100 ms ± 25 ms and 5% loss each way
static class NetProfiles
{
    // The profile's settings for the compose file, as PREFIX_DELAY, PREFIX_JITTER and PREFIX_LOSS.
    public static Dictionary<string, string?> Settings(string prefix, string profile)
    {
        var (delay, jitter, loss) = profile switch
        {
            "none" => ("0ms", "0ms", "0%"),
            "good" => ("40ms", "10ms", "1%"),
            "bad" => ("100ms", "25ms", "2%"),
            "awful" => ("100ms", "25ms", "5%"),
            _ => throw Tool.Fail($"Unknown network profile {profile} (none, good, bad or awful).", 2),
        };
        return new() { [$"{prefix}_DELAY"] = delay, [$"{prefix}_JITTER"] = jitter, [$"{prefix}_LOSS"] = loss };
    }

    // Fails unless the kernel modules netem needs are loaded. Only Linux runs Docker on this machine's own kernel;
    // Docker Desktop's VM (macOS, Windows) has them.
    public static void CheckModules()
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var module in new[] { "ifb", "sch_netem", "sch_ingress", "act_mirred", "cls_matchall" })
            if (!Directory.Exists($"/sys/module/{module}"))
                throw Tool.Fail($"Kernel module {module} isn't loaded; see README.md (Requirements, Docker).");
    }

    // The host user for the containers, so files they write in artifacts/ stay readable outside Docker. Docker
    // Desktop maps file owners itself, so Windows leaves it to the compose file's default.
    public static string? HostUser() =>
        OperatingSystem.IsWindows() ? null : $"{Tool.Output("id", "-u")}:{Tool.Output("id", "-g")}";

    // A connection string for this machine's comet-postgres, as containers on the comet network reach it.
    public static string ForContainers(string connection) =>
        connection.Replace("Host=127.0.0.1;Port=55432", "Host=comet-postgres;Port=5432");
}
