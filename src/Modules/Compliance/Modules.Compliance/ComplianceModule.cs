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
        return services;
    }

    /// <summary>Phase-2 stub: no holds exist until task 007 builds them.</summary>
    private sealed class NoHoldsStub : IHoldQuery
    {
        public Task<bool> HasActiveHoldAsync(HoldSubject subject, Guid subjectId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
