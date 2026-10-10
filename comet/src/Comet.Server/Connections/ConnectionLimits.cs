namespace Comet.Server.Connections;

/// <summary>
/// Limits on connections as a whole, so a client can't multiply the per-connection limits (<see cref="Connection"/>)
/// by opening many. Register one as a service to change them; the defaults apply otherwise.
/// </summary>
public sealed class ConnectionLimits
{
    /// <summary>Open connections at most; more are refused before the WebSocket is accepted.</summary>
    public int MaxConnections { get; set; } = 500;

    /// <summary>
    /// Open connections from one address at most (an IPv6 address counts by its /64, which one machine usually
    /// has whole).
    /// </summary>
    public int MaxConnectionsPerAddress { get; set; } = 8;

    /// <summary>
    /// How often the server sends a WebSocket ping frame. Browsers answer these themselves, even in a hidden tab
    /// that has stopped running the game, so only dead connections go quiet.
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a ping may go unanswered before the connection is dropped.</summary>
    public TimeSpan KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(15);
}
