using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Persistence;

namespace Modules.Organizations;

/// <summary>Composition entry point of the Organizations module; the hosts call it to register the module's services.</summary>
public static class OrganizationsModule
{
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<OrgsDbContext>(OrgsDbContext.SchemaName, migrationOrder: 3);
        services.AddOptions<OrganizationOptions>().BindConfiguration("Auth");
        services.AddScoped<IOrganizationQuery, OrganizationQuery>();
        services.AddScoped<OrganizationService>();
        return services;
    }

    /// <summary>The organization-context check; after authentication and authorization, before idempotency.</summary>
    public static IApplicationBuilder UseOrganizationContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OrganizationContextMiddleware>();
}
