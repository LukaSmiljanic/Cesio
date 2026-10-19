using Microsoft.Extensions.DependencyInjection;

namespace Modules.Notifications;

/// <summary>Composition entry point of the Notifications module; the hosts call it to register the module's services.</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services) => services;
}
