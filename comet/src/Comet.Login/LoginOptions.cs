namespace Comet.Login;

/// <summary>The Login server's settings (the <c>Login</c> configuration section). The game sets its own periods.</summary>
public sealed class LoginOptions
{
    public const string Section = "Login";

    /// <summary>The PostgreSQL connection string, as the database's owner (migrations create a collation in <c>public</c>).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>The Login server's node number, for its Snowflake IDs (apart from every game server's).</summary>
    public int NodeId { get; set; }

    /// <summary>The PEM file of the private key that signs join tickets.</summary>
    public string SigningKeyFile { get; set; } = "";

    /// <summary>
    /// Makes the signing key pair if <see cref="SigningKeyFile"/> is missing, writing the public key into
    /// <see cref="PublicKeysFolder"/> for game servers. For development.
    /// </summary>
    public bool CreateSigningKey { get; set; }

    /// <summary>Where <see cref="CreateSigningKey"/> writes the public key (named by its key ID).</summary>
    public string PublicKeysFolder { get; set; } = "";

    /// <summary>Applies migrations at startup (EF Core locks the database while it does).</summary>
    public bool Migrate { get; set; } = true;

    /// <summary>How long an unused session lasts; each use (at most once an hour) moves its expiry forward.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How new a session must be to change the password or make a recovery code.</summary>
    public TimeSpan RecentSignIn { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Unclaimed guests idle this long are deleted. Null keeps them.</summary>
    public TimeSpan? GuestIdle { get; set; }

    /// <summary>How long a deleted account stays (holding its username, restorable) before the purge. Deleted guests go at the next purge.</summary>
    public TimeSpan AccountHold { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long a deleted character stays before the purge.</summary>
    public TimeSpan CharacterHold { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the guest cleanup and the purge run.</summary>
    public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Wrong passwords for one account from one address before that pair is locked out.</summary>
    public int LockoutFailures { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The pages allowed to call the Login server from a browser (CORS), such as <c>https://shapeland.example.com</c>.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    public LoginRateLimits RateLimits { get; set; } = new();
}

/// <summary>Requests allowed per client address. The address comes from forwarded headers only from a trusted (local) proxy.</summary>
public sealed class LoginRateLimits
{
    /// <summary>Signing in with a password or recovery code, and restoring, per minute.</summary>
    public int SignInPerMinute { get; set; } = 5;

    public int GuestsPerHour { get; set; } = 10;

    /// <summary>Every other request, per minute.</summary>
    public int OtherPerMinute { get; set; } = 60;

    /// <summary>Addresses with no limits (local bots and load tests). Only listed addresses: never "every local address".</summary>
    public string[] ExemptAddresses { get; set; } = [];
}
