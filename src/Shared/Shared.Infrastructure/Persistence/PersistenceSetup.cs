using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence.Platform;

namespace Shared.Infrastructure.Persistence;

public static class PersistenceSetup
{
    public const string ConnectionStringName = "Cesio";

    /// <summary>EF migration history lives in each module's own schema, so modules migrate independently.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static string ConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured (see README, local development).");
    }

    /// <summary>Unit of work, the <c>platform</c> schema (outbox, idempotency, business parameters) and their services.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<UnitOfWork>();
        services.AddModuleDbContext<PlatformDbContext>(PlatformDbContext.SchemaName, migrationOrder: 0);
        services.AddOutbox();
        return services;
    }

    /// <summary>
    /// Registers a module DbContext on the scope's <see cref="UnitOfWork"/> connection and its migrations for
    /// Platform.Migrator. Lower <paramref name="migrationOrder"/> migrates first.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema, int migrationOrder)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<UnitOfWork>().Connection, sql => sql.MigrationsHistoryTable(MigrationsHistoryTable, schema)));
        services.AddSingleton<IModuleMigrator>(new ModuleMigrator<TContext>(schema, migrationOrder));
        return services;
    }

    /// <summary>A context on its own connection string, outside any unit of work: for migrations and design-time tools.</summary>
    public static TContext CreateStandalone<TContext>(string connectionString, string schema)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .Options;
        return (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
    }

    /// <summary>Design-time context for <c>dotnet ef migrations add</c>; it never connects.</summary>
    public static TContext CreateForDesignTime<TContext>(string schema)
        where TContext : DbContext =>
        CreateStandalone<TContext>("Server=design-time;Database=design-time", schema);
}

/// <summary>Applies one module's migrations; Platform.Migrator runs all of them in <see cref="Order"/>.</summary>
public interface IModuleMigrator
{
    string Schema { get; }

    int Order { get; }

    Task MigrateAsync(string connectionString, CancellationToken cancellationToken);
}

internal sealed class ModuleMigrator<TContext>(string schema, int order) : IModuleMigrator
    where TContext : DbContext
{
    public string Schema => schema;

    public int Order => order;

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var context = PersistenceSetup.CreateStandalone<TContext>(connectionString, schema);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
