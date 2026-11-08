namespace ShapeLand.GameServer;

public sealed class ShapeLandServerOptions
{
    public string ContentFile { get; set; } = "";

    public string StartSpawn { get; set; } = "";

    public string WebRoot { get; set; } = "";

    public string LoginUrl { get; set; } = "";

    /// <summary>
    /// This server's node number: join tickets name it, and its owner rows and Snowflake IDs carry it.
    /// </summary>
    public int ServerId { get; set; } = 1;

    public string ConnectionString { get; set; } = "";

    public string TicketKeysFolder { get; set; } = "";

    /// <summary>In seconds.</summary>
    public double SaveIntervalSeconds { get; set; } = 60;

    /// <summary>Debug builds only: a Release build refuses to start with it.</summary>
    public bool DevJoins { get; set; }

    public string DevCharactersFile { get; set; } = "";
}
