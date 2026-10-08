namespace ShapeLand.GameServer;

public sealed class ShapeLandServerOptions
{
    /// <summary>The compiled content file; empty means the content build's output in this repository's artifacts/.</summary>
    public string ContentFile { get; set; } = "";

    /// <summary>
    /// A web build to serve from the same port (a development option, so one command runs both and the page
    /// connects to the host it came from); empty serves none.
    /// </summary>
    public string WebRoot { get; set; } = "";
}
