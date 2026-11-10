public static class NetProfiles
{
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
        return new()
        {
            [$"{prefix}_DELAY"] = delay,
            [$"{prefix}_JITTER"] = jitter,
            [$"{prefix}_LOSS"] = loss,
        };
    }

    // Fails unless the kernel modules netem needs are loaded; only Linux runs Docker on this machine's own kernel.
    // Docker Desktop's macOS VM has netem but no ifb device, which shaping incoming traffic needs (tools/docker/netem.sh);
    // Windows uses its WSL kernel.
    public static void CheckModules()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine(
                "Simulated network runs are tested on Linux; Docker Desktop's VM may have no ifb device, and the "
                    + "server's container then stops at \"Unknown device type\"."
            );
            return;
        }
        foreach (var module in new[] { "ifb", "sch_netem", "sch_ingress", "act_mirred", "cls_matchall" })
            if (!Directory.Exists($"/sys/module/{module}"))
                throw Tool.Fail($"Kernel module {module} isn't loaded; see README.md (Requirements, Docker).");
    }

    public static string? HostUser() => OperatingSystem.IsWindows() ? null : $"{Tool.Output("id", "-u")}:{Tool.Output("id", "-g")}";

    public static string ForContainers(string connection) => connection.Replace("Host=127.0.0.1;Port=55432", "Host=comet-postgres;Port=5432");
}
