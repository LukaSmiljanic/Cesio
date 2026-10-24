using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance.Contracts;
using Modules.Documents.Contracts;
using Modules.Identity.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Marketplace.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Marketplace;

/// <summary>Composition entry point of the Marketplace module; the hosts call it to register the module's services.</summary>
public static class MarketplaceModule
{
    /// <summary>Feed and detail requests per user and minute (task 012 security considerations).</summary>
    public const string RateLimitPolicy = "marketplace";

    private const int RequestsPerMinute = 120;

    public static IServiceCollection AddMarketplaceModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<MarketplaceDbContext>(MarketplaceDbContext.SchemaName, migrationOrder: 6);
        services.AddScoped<ListingVisibility>();
        services.AddScoped<ListingService>();
        services.AddScoped<OfferService>();
        services.AddScoped<IListingWriter, ListingWriter>();
        services.AddScoped<IDocumentAccessPolicy, ListingDocumentPolicy>();
        services.AddIntegrationEventConsumer<FactorAuthorizationRevoked, FactorAuthorizationEndedConsumer>(FactorAuthorizationEndedConsumer.RevokedConsumerName);
        services.AddIntegrationEventConsumer<FactorAuthorizationExpired, FactorAuthorizationEndedConsumer>(FactorAuthorizationEndedConsumer.ExpiredConsumerName);
        services.Configure<RateLimiterOptions>(options => options.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirst(CesioClaims.UserId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = RequestsPerMinute, Window = TimeSpan.FromMinutes(1) })));
        return services;
    }

    /// <summary>Seller listings, the factor marketplace and the operations actions (task 012); offers (task 013).</summary>
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder app)
    {
        var seller = app.MapGroup("/api/v1").RequireAuthorization().RequireOrganization();
        seller.MapPost("/receivables/{id:guid}/listings", (Guid id, OpenListingRequest body, ListingService service, CancellationToken ct) =>
            service.OpenAsync(id, body, ct)).RequireIdempotency();
        seller.MapGet("/listings", (ListingService service, CancellationToken ct) => service.MineAsync(ct));
        seller.MapGet("/listings/{id:guid}", (Guid id, ListingService service, CancellationToken ct) => service.GetMineAsync(id, ct));
        seller.MapPost("/listings/{id:guid}/withdraw", (Guid id, ListingService service, CancellationToken ct) => service.WithdrawAsync(id, ct))
            .RequireIdempotency();

        var factor = app.MapGroup("/api/v1/marketplace").RequireAuthorization().RequireOrganization().RequireRateLimiting(RateLimitPolicy);
        factor.MapGet("/listings", ([AsParameters] FeedQuery query, ListingService service, CancellationToken ct) => service.FeedAsync(query, ct));
        factor.MapGet("/listings/{id:guid}", (Guid id, ListingService service, CancellationToken ct) => service.DetailAsync(id, ct));
        factor.MapGet("/listings/{id:guid}/fee-preview", (Guid id, string? purchasePrice, OfferService service, CancellationToken ct) =>
            service.FeePreviewAsync(id, purchasePrice, ct));
        factor.MapPost("/listings/{id:guid}/offers", (Guid id, SubmitOfferRequest body, OfferService service, CancellationToken ct) =>
            service.SubmitAsync(id, body, ct)).RequireIdempotency();

        seller.MapGet("/listings/{id:guid}/offers", (Guid id, OfferService service, CancellationToken ct) => service.ForListingAsync(id, ct));
        seller.MapGet("/offers", (OfferState? state, OfferService service, CancellationToken ct) => service.MineAsync(state, ct));
        seller.MapGet("/offers/{id:guid}", (Guid id, OfferService service, CancellationToken ct) => service.GetAsync(id, ct));
        seller.MapPost("/offers/{id:guid}/withdraw", (Guid id, OfferService service, CancellationToken ct) => service.WithdrawAsync(id, ct))
            .RequireIdempotency();
        seller.MapPost("/offers/{id:guid}/reject", (Guid id, RejectOfferRequest body, OfferService service, CancellationToken ct) =>
            service.RejectAsync(id, body, ct)).RequireIdempotency();

        var admin = app.MapGroup("/api/v1/admin/listings").RequireAuthorization().AddEndpointFilter(RequireOperations);
        admin.MapPost("/{id:guid}/suspend", (Guid id, ListingActionRequest body, ListingService service, CancellationToken ct) =>
            service.SuspendOrResumeAsync(id, suspend: true, body, ct)).RequireIdempotency();
        admin.MapPost("/{id:guid}/resume", (Guid id, ListingActionRequest body, ListingService service, CancellationToken ct) =>
            service.SuspendOrResumeAsync(id, suspend: false, body, ct)).RequireIdempotency();
        return app;
    }

    /// <summary>Admin endpoints are for operations staff only; anyone else gets 404 (no existence leak, security.md §3).</summary>
    private static async ValueTask<object?> RequireOperations(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && await roles.HasRoleAsync(userId, PlatformRoles.Operations, http.RequestAborted)
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
