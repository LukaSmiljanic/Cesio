using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Deals;

/// <summary>Composition entry point of the Deals module; the hosts call it to register the module's services.</summary>
public static class DealsModule
{
    public static IServiceCollection AddDealsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<DealsDbContext>(DealsDbContext.SchemaName, migrationOrder: 8);
        services.AddScoped<DealService>();
        return services;
    }

    /// <summary>Offer acceptance (Signatory, step-up) and the deal reads of the two parties (task 014).</summary>
    public static IEndpointRouteBuilder MapDealEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization().RequireOrganization();
        api.MapPost("/offers/{id:guid}/accept", (Guid id, AcceptOfferRequest body, HttpContext http, DealService service, CancellationToken ct) =>
            service.AcceptAsync(id, body, IfMatch(http), (DateTimeOffset)http.Items[CesioClaims.MfaStepUpAtItem]!, ct))
            .RequireIdempotency().RequireStepUp();
        api.MapGet("/deals", (DealService service, CancellationToken ct) => service.ListAsync(ct));
        api.MapGet("/deals/{id:guid}", (Guid id, DealService service, CancellationToken ct) => service.GetAsync(id, ct));
        api.MapGet("/deals/{id:guid}/timeline", (Guid id, DealService service, CancellationToken ct) => service.TimelineAsync(id, ct));
        return app;
    }

    /// <summary>The listing version from <c>If-Match: "3"</c>, if sent (api.md §1).</summary>
    private static int? IfMatch(HttpContext http) =>
        int.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"', ' '), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
}
