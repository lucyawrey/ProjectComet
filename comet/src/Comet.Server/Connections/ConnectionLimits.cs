namespace Comet.Server.Connections;

public sealed class ConnectionLimits
{
    public int MaxConnections { get; set; } = 500;

    /// <summary>Open connections from one address at most (an IPv6 address counts by its /64, which one machine usually has whole).</summary>
    public int MaxConnectionsPerAddress { get; set; } = 8;

    /// <summary>Browsers answer these even in a hidden tab that has stopped running the game, so only dead connections go quiet.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
