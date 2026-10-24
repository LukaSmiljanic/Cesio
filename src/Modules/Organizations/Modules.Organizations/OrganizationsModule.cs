using System.Threading.RateLimiting;
using Integrations.Sef;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Organizations;

/// <summary>Composition entry point of the Organizations module; the hosts call it to register the module's services.</summary>
public static class OrganizationsModule
{
    public const string ClaimRateLimitPolicy = "orgs-sef-claim";

    public const string BankAccountRateLimitPolicy = "orgs-bank-account";

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
        services.AddJob("orgs-draft-expiry", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<DraftExpiry>().ExpireAsync(ct));
        services.AddIntegrationEventConsumer<OrganizationApproved, ApprovalNotice>(ApprovalNotice.ConsumerName);
        // SEF claim attempts per user (task 007a security considerations): a valid key is needed anyway, this limits probing.
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(ClaimRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(CesioClaims.UserId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) })));
        services.AddScoped<BankAccountService>();
        services.AddScoped<IBankAccountQuery>(provider => provider.GetRequiredService<BankAccountService>());
        // Payout account additions per user (task 009 security considerations).
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(BankAccountRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(CesioClaims.UserId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) })));
        return services;
    }


    /// <summary>The organization-context check; after authentication and authorization, before idempotency.</summary>
    public static IApplicationBuilder UseOrganizationContext(this IApplicationBuilder app) =>
        app.UseMiddleware<OrganizationContextMiddleware>();
}
