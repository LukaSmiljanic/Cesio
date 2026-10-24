using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;
using Modules.Identity.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Notifications.Contracts;
using Modules.Notifications.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Notifications;

/// <summary>Composition entry point of the Notifications module; the hosts call it to register the module's services.</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName, migrationOrder: 9);
        services.AddScoped<NotificationService>();
        services.AddScoped<EmailDispatcher>();
        services.AddJob("notifications-email", TimeSpan.FromMinutes(1), (provider, ct) => provider.GetRequiredService<EmailDispatcher>().SendPendingAsync(ct));
        services.AddJob("notifications-digest", TimeSpan.FromHours(1), (provider, ct) => provider.GetRequiredService<EmailDispatcher>().SendDigestsAsync(ct));
        services.AddIntegrationEventConsumer<DealCreated, NotificationConsumers>(NotificationConsumers.Name(DealCreated.EventType));
        services.AddIntegrationEventConsumer<DealSigned, NotificationConsumers>(NotificationConsumers.Name(DealSigned.EventType));
        services.AddIntegrationEventConsumer<DealFundingReported, NotificationConsumers>(NotificationConsumers.Name(DealFundingReported.EventType));
        services.AddIntegrationEventConsumer<DealFunded, NotificationConsumers>(NotificationConsumers.Name(DealFunded.EventType));
        services.AddIntegrationEventConsumer<DealCancelled, NotificationConsumers>(NotificationConsumers.Name(DealCancelled.EventType));
        services.AddIntegrationEventConsumer<OfferSubmitted, NotificationConsumers>(NotificationConsumers.Name(OfferSubmitted.EventType));
        services.AddIntegrationEventConsumer<OfferAccepted, NotificationConsumers>(NotificationConsumers.Name(OfferAccepted.EventType));
        services.AddIntegrationEventConsumer<OfferSuperseded, NotificationConsumers>(NotificationConsumers.Name(OfferSuperseded.EventType));
        services.AddIntegrationEventConsumer<ListingOpened, NotificationConsumers>(NotificationConsumers.Name(ListingOpened.EventType));
        services.AddIntegrationEventConsumer<OrganizationApproved, NotificationConsumers>(NotificationConsumers.Name(OrganizationApproved.EventType));
        services.AddIntegrationEventConsumer<BankAccountAdded, NotificationConsumers>(NotificationConsumers.Name(BankAccountAdded.EventType));
        services.AddIntegrationEventConsumer<BankAccountVerified, NotificationConsumers>(NotificationConsumers.Name(BankAccountVerified.EventType));
        services.AddIntegrationEventConsumer<BankAccountDeactivated, NotificationConsumers>(NotificationConsumers.Name(BankAccountDeactivated.EventType));
        services.AddIntegrationEventConsumer<SefConnectionInvalidated, NotificationConsumers>(NotificationConsumers.Name(SefConnectionInvalidated.EventType));
        services.AddIntegrationEventConsumer<FactorAuthorizationRevoked, NotificationConsumers>(NotificationConsumers.Name(FactorAuthorizationRevoked.EventType));
        services.AddIntegrationEventConsumer<FactorAuthorizationExpired, NotificationConsumers>(NotificationConsumers.Name(FactorAuthorizationExpired.EventType));
        services.AddIntegrationEventConsumer<OrganizationNotificationRequested, NotificationConsumers>(NotificationConsumers.Name(OrganizationNotificationRequested.EventType));
        return services;
    }


    /// <summary><c>/notifications</c>, <c>/notification-preferences</c> (task 019) and the admin failure list.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/notifications", (DateTimeOffset? before, int? limit, NotificationService service, CancellationToken ct) => service.ListAsync(before, limit, ct));
        api.MapGet("/notifications/unread-count", (NotificationService service, CancellationToken ct) => service.UnreadCountAsync(ct));
        api.MapPost("/notifications/{id:guid}/read", (Guid id, NotificationService service, CancellationToken ct) => service.MarkReadAsync(id, ct)).RequireIdempotency();
        api.MapGet("/notification-preferences", (NotificationService service, CancellationToken ct) => service.PreferencesAsync(ct));
        api.MapPut("/notification-preferences", (PreferencesRequest body, NotificationService service, CancellationToken ct) => service.SetPreferencesAsync(body, ct))
            .RequireIdempotency();
        api.MapGet("/admin/notification-failures", (NotificationService service, CancellationToken ct) => service.FailuresAsync(ct)).AddEndpointFilter(RequireOperations);
        return app;
    }

    /// <summary>Integration health is for Operations and PlatformAdmin staff; anyone else gets 404.</summary>
    private static async ValueTask<object?> RequireOperations(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && (await roles.HasRoleAsync(userId, PlatformRoles.Operations, http.RequestAborted) || await roles.HasRoleAsync(userId, PlatformRoles.PlatformAdmin, http.RequestAborted))
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
