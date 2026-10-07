namespace ShapeLand.GameServer;

public sealed class ShapeLandServerOptions
{
    /// <summary>The compiled content file; empty means the content build's output in this repository's artifacts/.</summary>
    public string ContentFile { get; set; } = "";
}
