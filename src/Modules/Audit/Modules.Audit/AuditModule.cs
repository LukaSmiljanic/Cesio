using Microsoft.Extensions.DependencyInjection;

namespace Modules.Audit;

/// <summary>Composition entry point of the Audit module; the hosts call it to register the module's services.</summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services) => services;
}
