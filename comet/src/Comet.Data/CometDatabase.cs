using System.Data.Common;
using Comet.Data.Characters;
using Microsoft.EntityFrameworkCore;

namespace Comet.Data;

public static class CometDatabase
{
    public static DbContextOptionsBuilder UseComet(
        this DbContextOptionsBuilder options,
        string connectionString,
        string schema,
        TimeProvider clock
    ) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TimestampInterceptor(clock));

    /// <summary>For sharing another context's open <paramref name="connection"/> in one transaction.</summary>
    public static DbContextOptionsBuilder UseComet(
        this DbContextOptionsBuilder options,
        DbConnection connection,
        string schema,
        TimeProvider clock
    ) =>
        options
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TimestampInterceptor(clock));

    public static DbContextOptionsBuilder UseCometRegion(this DbContextOptionsBuilder options, string connectionString, TimeProvider clock) =>
        options.UseComet(connectionString, CometRegion.Schema, clock);
}
