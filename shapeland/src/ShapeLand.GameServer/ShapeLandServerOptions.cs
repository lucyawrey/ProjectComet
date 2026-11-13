namespace ShapeLand.GameServer;

public sealed class ShapeLandServerOptions
{
    public string ContentFile { get; set; } = "";

    public string StartSpawn { get; set; } = "";

    public string WebRoot { get; set; } = "";

    public string LoginUrl { get; set; } = "";

    public int ServerId { get; set; } = 1;

    public string ConnectionString { get; set; } = "";

    public string TicketKeysFolder { get; set; } = "";

    public double SaveIntervalSeconds { get; set; } = 60;

    public bool DevJoins { get; set; }

    public string DevCharactersFile { get; set; } = "";
}
