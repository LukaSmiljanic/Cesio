using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;
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
        services.AddScoped<DealStepService>();
        services.AddScoped<IDocumentAccessPolicy, DealDocumentPolicy>();
        return services;
    }

    /// <summary>Offer acceptance and the deal reads of the two parties (task 014); the steps before signing (task 015).</summary>
    public static IEndpointRouteBuilder MapDealEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization().RequireOrganization();
        api.MapPost("/offers/{id:guid}/accept", (Guid id, AcceptOfferRequest body, HttpContext http, DealService service, CancellationToken ct) =>
            service.AcceptAsync(id, body, IfMatch(http), StepUpAt(http), ct))
            .RequireIdempotency().RequireStepUp();
        api.MapGet("/deals", (DealService service, CancellationToken ct) => service.ListAsync(ct));
        api.MapGet("/deals/{id:guid}", (Guid id, DealService service, CancellationToken ct) => service.GetAsync(id, ct));
        api.MapGet("/deals/{id:guid}/timeline", (Guid id, DealService service, CancellationToken ct) => service.TimelineAsync(id, ct));
        api.MapGet("/deals/{id:guid}/kyb-package", (Guid id, DealStepService service, CancellationToken ct) => service.KybPackageAsync(id, ct));
        api.MapPost("/deals/{id:guid}/cdd-decision", (Guid id, CddDecisionRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.CddDecisionAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();
        api.MapPost("/deals/{id:guid}/contract", (Guid id, ContractRequest body, DealStepService service, CancellationToken ct) =>
            service.UploadContractAsync(id, body, ct)).RequireIdempotency();
        api.MapPost("/deals/{id:guid}/terms-confirmation", (Guid id, TermsConfirmationRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.ConfirmTermsAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();
        api.MapPost("/deals/{id:guid}/cancel", (Guid id, CancelDealRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.CancelAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>The session's step-up time; <c>RequireStepUp</c> already checked that it is recent.</summary>
    private static DateTimeOffset StepUpAt(HttpContext http) => (DateTimeOffset)http.Items[CesioClaims.MfaStepUpAtItem]!;

    /// <summary>The listing version from <c>If-Match: "3"</c>, if sent (api.md §1).</summary>
    private static int? IfMatch(HttpContext http) =>
        int.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"', ' '), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
}
