using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Compliance.Contracts;

namespace Modules.Compliance;

/// <summary>Composition entry point of the Compliance module; the hosts call it to register the module's services.</summary>
public static class ComplianceModule
{
    /// <summary>Environments where the phase-2 stubs may run (tasks/README, "Phase 2 stubs").</summary>
    private static readonly string[] StubEnvironments = ["Development", "local", "dev", "staging"];

    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<IHoldQuery>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return StubEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new NoHoldsStub()
                : throw new InvalidOperationException($"Holds (task 007) are not implemented; the stub must not run in '{environment}'.");
        });
        services.AddScoped<IAuthorizationQuery>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return StubEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                ? new AuthorizedFactorsStub()
                : throw new InvalidOperationException($"Factor authorization (task 008) is not implemented; the stub must not run in '{environment}'.");
        });
        return services;
    }

    /// <summary>
    /// Phase-2 stub: every factor organization counts as authorized until task 008 builds FactorAuthorization; callers
    /// still require the organization to be an Approved factor.
    /// </summary>
    private sealed class AuthorizedFactorsStub : IAuthorizationQuery
    {
        public Task<bool> IsFactorAuthorizedAsync(Guid factorOrgId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    /// <summary>Phase-2 stub: no holds exist until task 007 builds them.</summary>
    private sealed class NoHoldsStub : IHoldQuery
    {
        public Task<bool> HasActiveHoldAsync(HoldSubject subject, Guid subjectId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
