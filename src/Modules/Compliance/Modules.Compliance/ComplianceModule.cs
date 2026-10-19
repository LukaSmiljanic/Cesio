using Microsoft.Extensions.DependencyInjection;

namespace Modules.Compliance;

/// <summary>Composition entry point of the Compliance module; the hosts call it to register the module's services.</summary>
public static class ComplianceModule
{
    public static IServiceCollection AddComplianceModule(this IServiceCollection services) => services;
}
