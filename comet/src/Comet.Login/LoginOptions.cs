namespace Comet.Login;

public sealed class LoginOptions
{
    public const string Section = "Login";

    /// <summary>Must be the database's owner: migrations create a collation in <c>public</c>.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>For Snowflake IDs; must differ from every game server's.</summary>
    public int NodeId { get; set; }

    public string SigningKeyFile { get; set; } = "";

    public bool CreateSigningKey { get; set; }

    public string PublicKeysFolder { get; set; } = "";

    public bool Migrate { get; set; } = true;

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan RecentSignIn { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Unclaimed guests idle this long are deleted. Null keeps them.</summary>
    public TimeSpan? GuestIdle { get; set; }

    public TimeSpan AccountHold { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan CharacterHold { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromHours(1);

    public int LockoutFailures { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

    public string[] AllowedOrigins { get; set; } = [];

    public LoginRateLimits RateLimits { get; set; } = new();
}

public sealed class LoginRateLimits
{
    public int SignInPerMinute { get; set; } = 5;

    public int GuestsPerHour { get; set; } = 10;

    public int OtherPerMinute { get; set; } = 60;

    public string[] ExemptAddresses { get; set; } = [];
}
