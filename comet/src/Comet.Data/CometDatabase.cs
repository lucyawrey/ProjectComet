using System.Data.Common;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;

namespace Comet.Data;

/// <summary>Database options shared by every Comet context: PostgreSQL, snake_case names and the timestamps.</summary>
public static class CometDatabase
{
    /// <summary>Options for a context on <paramref name="schema"/>, keeping its migration history in that schema.</summary>
    public static DbContextOptionsBuilder UseComet(
        this DbContextOptionsBuilder options, string connectionString, string schema, TimeProvider clock) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TimestampInterceptor(clock));

    /// <summary>
    /// Options for a context on <paramref name="schema"/> that uses an open <paramref name="connection"/> (another
    /// context's), so the two can share one transaction (<c>UseTransaction</c>).
    /// </summary>
    public static DbContextOptionsBuilder UseComet(
        this DbContextOptionsBuilder options, DbConnection connection, string schema, TimeProvider clock) =>
        options
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TimestampInterceptor(clock));

    /// <summary>Options for a game's <see cref="CometRegionContext{TCharacter}"/>.</summary>
    public static DbContextOptionsBuilder UseCometRegion(
        this DbContextOptionsBuilder options, string connectionString, TimeProvider clock) =>
        options.UseComet(connectionString, CometRegion.Schema, clock);
}
