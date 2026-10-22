using Microsoft.Extensions.DependencyInjection;
using Modules.Audit.Contracts;
using Modules.Audit.Persistence;
using Shared.Infrastructure.Persistence;

namespace Modules.Audit;

/// <summary>Composition entry point of the Audit module; the hosts call it to register the module's services.</summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName, migrationOrder: 1);
        services.AddScoped<IAuditWriter, SqlAuditWriter>();
        services.AddScoped<IAccessLog, SqlAccessLog>();
        services.AddScoped<AuditChainVerifier>();
        return services;
    }

    /// <summary>The daily chain verification and digest job; Platform.Worker only.</summary>
    public static IServiceCollection AddAuditJobs(this IServiceCollection services)
    {
        services.AddHostedService<AuditDigestJob>();
        return services;
    }
}
