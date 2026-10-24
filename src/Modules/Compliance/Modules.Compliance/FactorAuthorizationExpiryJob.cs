using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Jobs;

namespace Modules.Compliance;

/// <summary>Daily: Verified factor authorizations past their re-verification date become Expired (task 008).</summary>
internal sealed class FactorAuthorizationExpiryJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<FactorAuthorizationExpiryJob> logger)
    : SingletonJob(scopeFactory, configuration, logger)
{
    protected override string LockName => "compliance-factor-authorization-expiry";

    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<FactorAuthorizationService>().ExpireDueAsync(cancellationToken);
}
