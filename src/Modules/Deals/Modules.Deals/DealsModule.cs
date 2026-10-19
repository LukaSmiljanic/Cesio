using Microsoft.Extensions.DependencyInjection;

namespace Modules.Deals;

/// <summary>Composition entry point of the Deals module; the hosts call it to register the module's services.</summary>
public static class DealsModule
{
    public static IServiceCollection AddDealsModule(this IServiceCollection services) => services;
}
