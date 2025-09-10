using System.Net;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AzaleaGames.ProjectComet.Api.Entities;

public class ApiDbContext : DbContext
{
    public DbSet<GameInfo> GameInfo { get; set; }
    public DbSet<User> User { get; set; }

    private readonly string _dbPath;
    private readonly IIdGenerator<long> _idGen;

    public ApiDbContext(IIdGenerator<long> idGen)
    {
        _idGen = idGen;

        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        _dbPath = Path.Join(path, "project_comet_api.db");
        Console.WriteLine("Database path: " + _dbPath);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TODO Add CreatedAt interface
        var entityTpes = modelBuilder.Model.GetEntityTypes().Where(t => t.ClrType.IsAssignableTo(typeof(IUpdated)));
        foreach (var entityType in entityTpes)
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property("UpdatedAt")
                .HasDefaultValueSql("unixepoch()")
                .HasConversion<DateTimeUnixEpochSecondsConverter>()
                .ValueGeneratedOnAddOrUpdate();
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is IGeneratedId entity)
                {
                    entity.Id = _idGen.CreateId();
                }
            }
            if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is IUpdated entity)
                {
                    entity.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        return base.SaveChangesAsync();
    }
}

public class DateTimeUnixEpochSecondsConverter : ValueConverter<DateTime, long>
{
    public DateTimeUnixEpochSecondsConverter()
        : base(
            v => ((DateTimeOffset)v).ToUnixTimeSeconds(),
            v => DateTimeOffset.FromUnixTimeSeconds(v).UtcDateTime)
    {
    }
}

public interface IUpdated
{
    DateTime UpdatedAt { get; set; }
}

public interface IGeneratedId
{
    long Id { get; set; }
}

public class GameInfo : IUpdated
{
    public long Id { get; set; } = 0; // Primary key. Enforce constraint Id = 0 (there should only ever be one GameInfo)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required string ServerGroupName { get; set; }
    public required string GameId { get; set; }
    public required string GameVersion { get; set; }
    public required List<string> SupportedClientGameIds { get; set; }
    public required List<string> SupportedClientGameVersions { get; set; }
    public required string GameDisplayName { get; set; }
}

public class World : IUpdated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsOverflowWorld { get; set; } = false;
    public required string DisplayName;

}

public class GameServer : IUpdated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required IPAddress Address;
    public int CurrentUserCount = 0;
}

public class Instance : IUpdated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CurrentUserCount = 0;
    public required string ZoneId;
    public required World World;
    public required GameServer GameServer;
}

public class User : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required long Handle { get; set; }
    public required string Username { get; set; }
    public Role Role { get; set; } = Role.NewPlayer;

    /// <summary>
    /// The user's hashed password. This is very sensitive information and should never be sent to a client.
    /// </summary>
    public required string PasswordHash { get; set; }
    /// <summary>
    /// The user's account recovery code. This is very sensitive information and should never be sent to a client.
    /// </summary>
    public string? RecoveryCode { get; set; }
}

public class Session
{
    public required string Id { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required User User { get; set; }
}

public class AccessToken
{

}

public enum Role
{
    NewPlayer = 0, Player = 1, MembershipPlayer = 2, GameModerator = 3, GameAdministrator = 4
}
