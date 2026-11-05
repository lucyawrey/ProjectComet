using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Comet.Data.Tests.PostgresContainer))]

namespace Comet.Data.Tests;

/// <summary>A game's character, as tests see it: Comet's columns plus one of the game's own.</summary>
public class TestCharacter : Character
{
    public required int Look { get; set; }
}

public sealed class TestRegionContext(DbContextOptions<TestRegionContext> options) : CometRegionContext<TestCharacter>(options);

/// <summary>
/// One throwaway PostgreSQL 18 container for the test run (Testcontainers; needs Docker). Each test makes its own
/// database in it.
/// </summary>
public sealed class PostgresContainer : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Makes a fresh, empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
