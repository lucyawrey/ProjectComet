using System.Net;
using System.Threading.RateLimiting;
using Comet.Data;
using Comet.Data.Accounts;
using Comet.Data.Characters;
using Comet.Protocol.Login;
using Comet.Tickets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Comet.Login;

/// <summary>Hosting <c>Comet.Login</c> in a game's Login server.</summary>
public static class CometLoginExtensions
{
    private const string CorsPolicy = "comet-login";

    /// <summary>
    /// Adds the Login server's services: the game's two contexts and rules, Identity (through
    /// <see cref="CometUserStore"/>), ticket signing, rate limits, CORS, startup migrations and the cleanup job. The
    /// settings come from the <c>Login</c> configuration section; <paramref name="configure"/> sets the game's policy
    /// on top.
    /// </summary>
    public static WebApplicationBuilder AddCometLogin<TAccounts, TRegion, TCharacter, TRules>(
        this WebApplicationBuilder builder,
        Action<LoginOptions>? configure = null
    )
        where TAccounts : CometAccountContext
        where TRegion : CometRegionContext<TCharacter>
        where TCharacter : Character
        where TRules : class, ILoginRules<TCharacter>
    {
        var services = builder.Services;
        var settings = new LoginOptions();
        builder.Configuration.GetSection(LoginOptions.Section).Bind(settings);
        configure?.Invoke(settings);
        services.AddSingleton(Options.Create(settings));

        // A JSON null for a field that can't be null is a bad request (400), not a crash further in.
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.RespectNullableAnnotations = true);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new SnowflakeIds(settings.NodeId, sp.GetRequiredService<TimeProvider>()));
        services.AddDbContext<TAccounts>((sp, options) => options.UseCometAccount(settings.ConnectionString, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<CometAccountContext>(sp => sp.GetRequiredService<TAccounts>());
        services.AddSingleton<RegionContexts<TCharacter>>(sp => new RegionContexts<TCharacter, TRegion>(
            settings.ConnectionString,
            sp.GetRequiredService<TimeProvider>()
        ));

        services.AddSingleton<ILoginRules<TCharacter>, TRules>();
        services.AddSingleton<SignInLockout>();
        services.AddSingleton<AccountActions<TCharacter>>();
        services.AddScoped<LoginAccounts<TCharacter>>();
        services.AddSingleton(sp => new TicketIssuer(LoadSigningKey(settings), sp.GetRequiredService<TimeProvider>()));

        services
            .AddIdentityCore<Account>(identity =>
            {
                // Current NIST guidance: a minimum length and nothing else. Lockout is SignInLockout's, per address.
                identity.Password = new PasswordOptions
                {
                    RequiredLength = 8,
                    RequiredUniqueChars = 1,
                    RequireDigit = false,
                    RequireLowercase = false,
                    RequireUppercase = false,
                    RequireNonAlphanumeric = false,
                };
                identity.User.AllowedUserNameCharacters = ""; // the game's rules check names
                identity.Lockout.AllowedForNewUsers = false;
            })
            .AddUserStore<CometUserStore>();

        services.AddHostedService<Startup<TCharacter>>();
        services.AddHostedService<Cleanup<TCharacter>>();

        services.AddProblemDetails();
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            // Trusts only the default known proxies (this machine's loopback), where Caddy runs.
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });
        services.AddCors(cors =>
            cors.AddPolicy(
                CorsPolicy,
                policy => policy.WithOrigins(settings.AllowedOrigins).WithMethods("GET", "POST").WithHeaders("Authorization", "Content-Type")
            )
        );
        services.AddRateLimiter(limits => AddRateLimits(limits, settings.RateLimits));
        return builder;
    }

    /// <summary>Adds the middleware (forwarded headers, CORS, rate limits) and maps the endpoints.</summary>
    public static WebApplication MapCometLogin<TCharacter>(this WebApplication app)
        where TCharacter : Character
    {
        app.UseForwardedHeaders();
        app.UseCors(CorsPolicy);
        app.UseRateLimiter();
        LoginEndpoints.Map<TCharacter>(app);
        return app;
    }

    private static void AddRateLimits(RateLimiterOptions limits, LoginRateLimits settings)
    {
        var exempt = settings.ExemptAddresses.Select(IPAddress.Parse).ToHashSet();
        RateLimitPartition<string> PerAddress(HttpContext http, string policy, int permits, TimeSpan window)
        {
            var address = http.Connection.RemoteIpAddress;
            if (address != null && (exempt.Contains(address) || (address.IsIPv4MappedToIPv6 && exempt.Contains(address.MapToIPv4()))))
            {
                return RateLimitPartition.GetNoLimiter($"{policy}:exempt");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{policy}:{address}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = window,
                    QueueLimit = 0,
                }
            );
        }

        limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            PerAddress(http, "other", settings.OtherPerMinute, TimeSpan.FromMinutes(1))
        );
        limits.AddPolicy(LoginEndpoints.SignInPolicy, http => PerAddress(http, "sign-in", settings.SignInPerMinute, TimeSpan.FromMinutes(1)));
        limits.AddPolicy(LoginEndpoints.GuestPolicy, http => PerAddress(http, "guest", settings.GuestsPerHour, TimeSpan.FromHours(1)));
        limits.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await LoginEndpoints.Problem(new(StatusCodes.Status429TooManyRequests, LoginErrors.RateLimited)).ExecuteAsync(context.HttpContext);
        };
    }

    private static Microsoft.IdentityModel.Tokens.ECDsaSecurityKey LoadSigningKey(LoginOptions settings)
    {
        if (settings.CreateSigningKey && !File.Exists(settings.SigningKeyFile))
        {
            var (privatePem, publicPem) = TicketKeys.Create();
            var publicKey = TicketKeys.PublicKey(publicPem);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settings.SigningKeyFile))!);
            Directory.CreateDirectory(settings.PublicKeysFolder);
            File.WriteAllText(Path.Combine(settings.PublicKeysFolder, $"{publicKey.KeyId}.pem"), publicPem);
            var privateFile = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                privateFile.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // the owner only
            }

            using var writer = new StreamWriter(settings.SigningKeyFile, privateFile);
            writer.Write(privatePem);
        }

        return TicketKeys.PrivateKey(File.ReadAllText(settings.SigningKeyFile));
    }
}
