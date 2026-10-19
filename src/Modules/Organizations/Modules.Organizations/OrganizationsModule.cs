using Microsoft.Extensions.DependencyInjection;

namespace Modules.Organizations;

/// <summary>Composition entry point of the Organizations module; the hosts call it to register the module's services.</summary>
public static class OrganizationsModule
{
    public static IServiceCollection AddOrganizationsModule(this IServiceCollection services) => services;
}
