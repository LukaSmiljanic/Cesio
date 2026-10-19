using Microsoft.Extensions.DependencyInjection;

namespace Modules.Fees;

/// <summary>Composition entry point of the Fees module; the hosts call it to register the module's services.</summary>
public static class FeesModule
{
    public static IServiceCollection AddFeesModule(this IServiceCollection services) => services;
}
