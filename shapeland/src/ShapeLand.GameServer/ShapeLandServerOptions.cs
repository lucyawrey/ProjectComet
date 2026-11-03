namespace ShapeLand.GameServer;

/// <summary>The game server's settings, from the <c>ShapeLand</c> section of its configuration (command line, environment or appsettings).</summary>
public sealed class ShapeLandServerOptions
{
    /// <summary>The compiled content file; empty means the content build's output in this repository's artifacts/.</summary>
    public string ContentFile { get; set; } = "";

    /// <summary>
    /// A spawn point (<c>spawn.*</c>) every player starts at, in place of their saved spot or their shape's spawn,
    /// for tests and for trying a level; empty for none.
    /// </summary>
    public string StartSpawn { get; set; } = "";

    /// <summary>
    /// A web build to serve from the same port (a development option, so one command runs both and the page
    /// connects to the host it came from); empty serves none.
    /// </summary>
    public string WebRoot { get; set; } = "";

    /// <summary>
    /// The Login server's address (<c>http://localhost:5090</c>), for the page this server serves (<see cref="WebRoot"/>):
    /// its <c>config.js</c> names it, as the deploy's does.
    /// </summary>
    public string LoginUrl { get; set; } = "";

    /// <summary>This server's node number: join tickets name it, and its owner rows and Snowflake IDs carry it.</summary>
    public int ServerId { get; set; } = 1;

    /// <summary>The PostgreSQL connection string for the region database (the Login server migrates it).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>The folder of the Login server's public keys (<c>*.pem</c>); empty means this repository's artifacts/keys/public.</summary>
    public string TicketKeysFolder { get; set; } = "";

    /// <summary>How often moved players' positions are saved and held characters checked (kicks), in seconds.</summary>
    public double SaveIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Joins without accounts, for the Unity editor's own server: clients join by name (<c>DevJoinRequest</c>), and
    /// the characters live in memory, kept in <see cref="DevCharactersFile"/>, so no Login server, PostgreSQL or
    /// tickets are needed. Debug builds only: a Release build refuses to start with it.
    /// </summary>
    public bool DevJoins { get; set; }

    /// <summary>With <see cref="DevJoins"/>, the snapshot file the characters and their spots are kept in; empty for memory only.</summary>
    public string DevCharactersFile { get; set; } = "";
}
