namespace ShapeLand.GameServer;

/// <summary>The game server's settings, from the <c>ShapeLand</c> section of its configuration (command line, environment or appsettings).</summary>
public sealed class ShapeLandServerOptions
{
    /// <summary>The compiled content file; empty means the content build's output in this repository's artifacts/.</summary>
    public string ContentFile { get; set; } = "";

    /// <summary>
    /// A spawn point (<c>spawn.*</c>) every new player starts at, in place of their shape's own, for tests and for
    /// trying a level; empty for none.
    /// </summary>
    public string StartSpawn { get; set; } = "";

    /// <summary>
    /// A web build to serve from the same port (a development option, so one command runs both and the page
    /// connects to the host it came from); empty serves none.
    /// </summary>
    public string WebRoot { get; set; } = "";
}
