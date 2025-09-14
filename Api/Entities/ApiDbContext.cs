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
        var entityTypes = modelBuilder.Model.GetEntityTypes().Where(t => t.ClrType.IsAssignableTo(typeof(IUpdated)));
        foreach (var entityType in entityTypes)
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property("UpdatedAt")
                .HasDefaultValueSql("unixepoch()")
                .HasConversion<DateTimeUnixEpochSecondsConverter>()
                .ValueGeneratedOnAddOrUpdate();
        }
        var entityTypesCreated = modelBuilder.Model.GetEntityTypes().Where(t => t.ClrType.IsAssignableTo(typeof(ICreated)));
        foreach (var entityType in entityTypesCreated)
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property("CreatedAt")
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
                if (entry.Entity is IGeneratedHandle handledEntity)
                {
                    handledEntity.Handle = _idGen.CreateId();
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

public interface ICreated
{
    DateTime CreatedAt { get; set; }
}

public interface IUpdated
{
    DateTime UpdatedAt { get; set; }
}

public interface IGeneratedId
{
    long Id { get; set; }
}

public interface IGeneratedHandle
{
    long Handle { get; set; }
}
