using Microsoft.Extensions.Hosting;
using Integrations.Sef;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Persistence;

namespace Modules.Organizations;

/// <summary>Composition entry point of the Organizations module; the hosts call it to register the module's services.</summary>
public static class OrganizationsModule
{
    /// <summary>Environments where the phase-2 stubs may run (tasks/README, "Phase 2 stubs").</summary>
    private static readonly string[] StubEnvironments = ["Development", "local", "dev", "staging"];

    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<OrgsDbContext>(OrgsDbContext.SchemaName, migrationOrder: 3);
        services.AddOptions<OrganizationOptions>().BindConfiguration("Auth");
        services.AddScoped<IOrganizationQuery, OrganizationQuery>();
        services.AddScoped<IOrganizationOnboarding, OrganizationOnboarding>();
        services.AddScoped<OrganizationService>();
        services.AddSef();
        services.AddScoped<SefConnectionService>();
        services.AddScoped<ISefConnections>(provider => provider.GetRequiredService<SefConnectionService>());
        services.AddScoped<IBankAccountQuery>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return StubEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new DevelopmentBankAccounts()
                : throw new InvalidOperationException($"Payout accounts (task 009) are not implemented; the stub must not run in '{environment}'.");
        });
        return services;
    }

    /// <summary>The organization-context check; after authentication and authorization, before idempotency.</summary>
    public static IApplicationBuilder UseOrganizationContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OrganizationContextMiddleware>();
}
