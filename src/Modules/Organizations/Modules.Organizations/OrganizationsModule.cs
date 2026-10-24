using System.Threading.RateLimiting;
using Integrations.Sef;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Organizations;

/// <summary>Composition entry point of the Organizations module; the hosts call it to register the module's services.</summary>
public static class OrganizationsModule
{
    /// <summary>Environments where the phase-2 stubs may run (tasks/README, "Phase 2 stubs").</summary>
    public const string ClaimRateLimitPolicy = "orgs-sef-claim";

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
        services.AddSingleton<IPristupnicaRenderer, SimplePdfRenderer>();
        services.AddScoped<ConfirmationTokens>();
        services.AddScoped<ConfirmationService>();
        services.AddScoped<PristupnicaService>();
        services.AddScoped<MfaResetService>();
        services.AddScoped<DraftExpiry>();
        services.AddIntegrationEventConsumer<OrganizationApproved, ApprovalNotice>(ApprovalNotice.ConsumerName);
        // SEF claim attempts per user (task 007a security considerations): a valid key is needed anyway, this limits probing.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(ClaimRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(CesioClaims.UserId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) })));
        services.AddScoped<IBankAccountQuery>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return StubEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new DevelopmentBankAccounts()
                : throw new InvalidOperationException($"Payout accounts (task 009) are not implemented; the stub must not run in '{environment}'.");
        });
        return services;
    }

    /// <summary>The daily jobs of the module; the worker calls it.</summary>
    public static IServiceCollection AddOrganizationsJobs(this IServiceCollection services)
    {
        services.AddHostedService<DraftExpiryJob>();
        return services;
    }

    /// <summary>The organization-context check; after authentication and authorization, before idempotency.</summary>
    public static IApplicationBuilder UseOrganizationContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OrganizationContextMiddleware>();
}
