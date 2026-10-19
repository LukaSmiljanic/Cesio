using Microsoft.Extensions.DependencyInjection;

namespace Modules.Receivables;

/// <summary>Composition entry point of the Receivables module; the hosts call it to register the module's services.</summary>
public static class ReceivablesModule
{
    public static IServiceCollection AddReceivablesModule(this IServiceCollection services) => services;
}
