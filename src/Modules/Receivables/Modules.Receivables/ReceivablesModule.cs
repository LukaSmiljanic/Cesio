using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Identity.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Receivables;

/// <summary>Composition entry point of the Receivables module; the hosts call it to register the module's services.</summary>
public static class ReceivablesModule
{
    public static IServiceCollection AddReceivablesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ReceivablesDbContext>(ReceivablesDbContext.SchemaName, migrationOrder: 5);
        services.AddScoped<ImportService>();
        services.AddScoped<VerificationService>();
        services.AddScoped<SefStatusPoller>();
        services.AddScoped<ReceivableMarket>();
        services.AddScoped<IReceivableMarket>(provider => provider.GetRequiredService<ReceivableMarket>());
        services.AddScoped<IReceivableWriter>(provider => provider.GetRequiredService<ReceivableMarket>());

        // The check pipeline of task 011, in the order of the task's table.
        services.AddScoped<IReceivableCheck, SefFreshApprovedCheck>();
        services.AddScoped<IReceivableCheck, SellerIsIssuerCheck>();
        services.AddScoped<IReceivableCheck, SellerApprovedCheck>();
        services.AddScoped<IReceivableCheck, NotMaturedCheck>();
        services.AddScoped<IReceivableCheck, MaxTenorCheck>();
        services.AddScoped<IReceivableCheck, AmountLimitsCheck>();
        services.AddScoped<IReceivableCheck, FaceValueConsistentCheck>();
        services.AddScoped<IReceivableCheck, DebtorScreeningCheck>();
        services.AddScoped<IReceivableCheck, DebtorNotSellerCheck>();
        services.AddScoped<IReceivableCheck, RelatedPartyCheck>();
        services.AddScoped<IReceivableCheck, DuplicateFingerprintCheck>();
        services.AddScoped<IReceivableCheck, NewDebtorForSellerCheck>();
        services.AddScoped<IReceivableCheck, DeclarationCompleteCheck>();
        return services;
    }

    /// <summary>Seller endpoints (tasks 010, 011) and the compliance review queue (task 011).</summary>
    public static IEndpointRouteBuilder MapReceivableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/receivables").RequireAuthorization().RequireOrganization();
        group.MapPost("/import-from-sef", (ImportFromSefRequest body, ImportService service, CancellationToken ct) => service.ImportAsync(body, ct))
            .RequireIdempotency();
        group.MapGet("", (ImportService service, CancellationToken ct) => service.ListAsync(ct));
        group.MapGet("/{id:guid}", (Guid id, ImportService service, CancellationToken ct) => service.GetAsync(id, ct));
        group.MapGet("/declaration", (VerificationService service, CancellationToken ct) => service.CurrentDeclarationAsync(ct));
        group.MapPut("/{id:guid}/declaration", (Guid id, DeclarationRequest body, VerificationService service, CancellationToken ct) =>
            service.SaveDeclarationAsync(id, body, ct)).RequireIdempotency();
        group.MapPost("/{id:guid}/documents", (Guid id, AttachDocumentRequest body, VerificationService service, CancellationToken ct) =>
            service.AttachDocumentAsync(id, body, ct)).RequireIdempotency();
        group.MapPost("/{id:guid}/submit", (Guid id, VerificationService service, CancellationToken ct) => service.SubmitAsync(id, ct))
            .RequireIdempotency();
        group.MapPost("/{id:guid}/cancel", (Guid id, VerificationService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .RequireIdempotency();
        group.MapGet("/{id:guid}/checks", (Guid id, VerificationService service, CancellationToken ct) => service.ChecksAsync(id, ct));

        var admin = app.MapGroup("/api/v1/admin/receivables").RequireAuthorization().AddEndpointFilter(RequireComplianceOfficer);
        admin.MapGet("/review-queue", (VerificationService service, CancellationToken ct) => service.ReviewQueueAsync(ct));
        admin.MapGet("/{id:guid}", (Guid id, VerificationService service, CancellationToken ct) => service.ReviewDetailAsync(id, ct));
        admin.MapPost("/{id:guid}/decision", (Guid id, ReviewDecisionRequest body, VerificationService service, CancellationToken ct) =>
            service.DecideAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>Admin endpoints are for compliance staff only; anyone else gets 404 (no existence leak, security.md §3).</summary>
    private static async ValueTask<object?> RequireComplianceOfficer(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && await roles.HasRoleAsync(userId, PlatformRoles.ComplianceOfficer, http.RequestAborted)
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
