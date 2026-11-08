namespace Comet.Login;

public sealed class LoginOptions
{
    public const string Section = "Login";

    /// <summary>Must be the database's owner: migrations create a collation in <c>public</c>.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>For Snowflake IDs; must differ from every game server's.</summary>
    public int NodeId { get; set; }

    public string SigningKeyFile { get; set; } = "";

    /// <summary>
    /// For development: makes the key pair if <see cref="SigningKeyFile"/> is missing, writing the public key into
    /// <see cref="PublicKeysFolder"/>.
    /// </summary>
    public bool CreateSigningKey { get; set; }

    public string PublicKeysFolder { get; set; } = "";

    public bool Migrate { get; set; } = true;

    /// <summary>How long an unused session lasts; each use moves its expiry forward.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How new a session must be to change the password or make a recovery code.</summary>
    public TimeSpan RecentSignIn { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Unclaimed guests idle this long are deleted. Null keeps them.</summary>
    public TimeSpan? GuestIdle { get; set; }

    /// <summary>Deleted guests skip it and go at the next purge.</summary>
    public TimeSpan AccountHold { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan CharacterHold { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Wrong passwords for one account from one address before that pair is locked out.</summary>
    public int LockoutFailures { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>CORS origins, such as <c>https://shapeland.example.com</c>.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    public LoginRateLimits RateLimits { get; set; } = new();
}

/// <summary>The client address comes from forwarded headers only when they're from a trusted (local) proxy.</summary>
public sealed class LoginRateLimits
{
    public int SignInPerMinute { get; set; } = 5;

    public int GuestsPerHour { get; set; } = 10;

    public int OtherPerMinute { get; set; } = 60;

    /// <summary>Addresses with no limits (local bots, load tests). Never "every local address".</summary>
    public string[] ExemptAddresses { get; set; } = [];
}
