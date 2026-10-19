using Microsoft.Extensions.DependencyInjection;

namespace Modules.Identity;

/// <summary>Composition entry point of the Identity module; the hosts call it to register the module's services.</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services) => services;
}
