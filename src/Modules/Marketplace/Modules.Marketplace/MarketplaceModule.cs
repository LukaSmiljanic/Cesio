using Microsoft.Extensions.DependencyInjection;

namespace Modules.Marketplace;

/// <summary>Composition entry point of the Marketplace module; the hosts call it to register the module's services.</summary>
public static class MarketplaceModule
{
    public static IServiceCollection AddMarketplaceModule(this IServiceCollection services) => services;
}
