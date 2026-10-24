using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Infrastructure.Secrets;

namespace Shared.Infrastructure;

public static class HostSetup
{
    public const string ReadyTag = "ready";

    /// <summary>Persistence, the HTTP request context and the database readiness check for Platform.Api.</summary>
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddPersistence();
        services.AddHttpContextAccessor();
        services.AddEmail();
        services.AddSecrets();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);
        return services;
    }

    /// <summary>Persistence, the system request context, the outbox dispatcher and the platform jobs for Platform.Worker.</summary>
    public static IServiceCollection AddWorkerInfrastructure(this IServiceCollection services)
    {
        services.AddPersistence();
        services.AddScoped<IRequestContext, SystemRequestContext>();
        services.AddSecrets();
        services.AddHostedService<OutboxDispatcherService>();
        services.AddHostedService<IdempotencyCleanupJob>();
        services.AddJobScheduler();
        return services;
    }
}

/// <summary>Ready only when the database answers (task 003: <c>/health/ready</c> includes the DB).</summary>
internal sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(PersistenceSetup.ConnectionString(configuration));
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}
