using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;
using Modules.Identity.Contracts;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Compliance;

/// <summary>Composition entry point of the Compliance module; the hosts call it to register the module's services.</summary>
public static class ComplianceModule
{
    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<ComplianceDbContext>(ComplianceDbContext.SchemaName, migrationOrder: 4);
        services.AddScoped<HoldService>();
        services.AddScoped<IHoldQuery>(provider => provider.GetRequiredService<HoldService>());
        services.AddScoped<IHoldWriter>(provider => provider.GetRequiredService<HoldService>());
        services.AddScoped<ScreeningService>();
        services.AddScoped<IScreeningService>(provider => provider.GetRequiredService<ScreeningService>());
        services.AddScoped<KybService>();
        services.AddScoped<IKybQuery>(provider => provider.GetRequiredService<KybService>());
        services.AddIntegrationEventConsumer<OrganizationSubmitted, KybService>(KybService.ConsumerName);
        services.AddScoped<FactorAuthorizationService>();
        services.AddJob("compliance-factor-authorization-expiry", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<FactorAuthorizationService>().ExpireDueAsync(ct));
        services.AddJob("compliance-factor-authorization-reminders", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<FactorAuthorizationService>().RemindReverificationAsync(ct));
        services.AddJob("compliance-kyb-periodic-review", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<KybService>().OpenPeriodicReviewsAsync(ct));
        services.AddJob("compliance-sanctions-rescreen", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<KybService>().RescreenApprovedAsync(ct));
        services.AddScoped<IAuthorizationQuery, FactorAuthorizationQuery>();
        return services;
    }


    /// <summary>The organization's KYB (task 007) and the compliance staff endpoints: KYB cases, screening lists and holds.</summary>
    public static IEndpointRouteBuilder MapComplianceEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/v1/organizations/{id:guid}/kyb").RequireAuthorization().RequireOrganization("id");
        customer.MapGet("", (Guid id, KybService service, CancellationToken ct) => service.CustomerViewAsync(id, ct));
        customer.MapPost("/beneficial-owners", (Guid id, BeneficialOwnersRequest body, KybService service, CancellationToken ct) =>
            service.DeclareOwnersAsync(id, body, ct)).RequireIdempotency();
        var factor = app.MapGroup("/api/v1/organizations/{id:guid}/factor-authorizations").RequireAuthorization().RequireOrganization("id");
        factor.MapGet("", (Guid id, FactorAuthorizationService service, CancellationToken ct) => service.ListMineAsync(id, ct));
        factor.MapPost("", (Guid id, FactorAuthorizationRequest body, FactorAuthorizationService service, CancellationToken ct) =>
            service.SubmitAsync(id, body, ct)).RequireIdempotency();
        customer.MapPost("/sharing-consent", (Guid id, ConsentRequest body, KybService service, CancellationToken ct) =>
            service.ConsentAsync(id, body, ct)).RequireIdempotency();

        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization().AddEndpointFilter(RequireComplianceOfficer);
        admin.MapGet("/kyb-cases", (string? status, KybService service, CancellationToken ct) => service.QueueAsync(status, ct));
        admin.MapGet("/kyb-cases/{id:guid}", (Guid id, KybService service, CancellationToken ct) => service.DetailAsync(id, ct));
        admin.MapPost("/kyb-cases/{id:guid}/screening", (Guid id, KybService service, CancellationToken ct) => service.RescreenAsync(id, ct)).RequireIdempotency();
        admin.MapPost("/kyb-cases/{id:guid}/screening/{resultId:guid}/resolution", (Guid id, Guid resultId, ScreeningResolutionRequest body, KybService service, CancellationToken ct) =>
            service.ResolveScreeningAsync(id, resultId, body, ct)).RequireIdempotency();
        admin.MapPost("/kyb-cases/{id:guid}/risk", (Guid id, RiskRatingRequest body, KybService service, CancellationToken ct) =>
            service.RateRiskAsync(id, body, ct)).RequireIdempotency();
        admin.MapPost("/kyb-cases/{id:guid}/decision", (Guid id, KybDecisionRequest body, KybService service, CancellationToken ct) =>
            service.DecideAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        admin.MapGet("/screening-lists", (ScreeningService service, CancellationToken ct) => service.ListsAsync(ct));
        admin.MapPost("/screening-lists/{source}", async (string source, HttpRequest httpRequest, ScreeningService service, CancellationToken ct) =>
            await service.ImportAsync(source, httpRequest.HasFormContentType ? (await httpRequest.ReadFormAsync(ct)).Files["file"] : null, ct)).RequireIdempotency();
        admin.MapGet("/factor-authorizations", (string? status, FactorAuthorizationService service, CancellationToken ct) => service.QueueAsync(status, ct));
        admin.MapPost("/factor-authorizations/{id:guid}/snapshot", async (Guid id, HttpRequest httpRequest, FactorAuthorizationService service, CancellationToken ct) =>
            await service.UploadSnapshotAsync(id, httpRequest.HasFormContentType ? (await httpRequest.ReadFormAsync(ct)).Files["file"] : null, ct)).RequireIdempotency();
        admin.MapPost("/factor-authorizations/{id:guid}/approve", (Guid id, FactorAuthorizationApproval body, FactorAuthorizationService service, CancellationToken ct) =>
            service.ApproveAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        admin.MapPost("/factor-authorizations/{id:guid}/reject", (Guid id, ReasonRequest body, FactorAuthorizationService service, CancellationToken ct) =>
            service.RejectAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        admin.MapPost("/factor-authorizations/{id:guid}/revoke", (Guid id, ReasonRequest body, FactorAuthorizationService service, CancellationToken ct) =>
            service.RevokeAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        admin.MapGet("/holds", (HoldService service, CancellationToken ct) => service.ListAsync(ct));
        admin.MapPost("/holds", (PlaceHoldRequest body, HoldService service, CancellationToken ct) => service.PlaceAsync(body, ct)).RequireIdempotency().RequireStepUp();
        admin.MapPost("/holds/{id:guid}/lift", (Guid id, HoldService service, CancellationToken ct) => service.LiftAsync(id, ct)).RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>Compliance endpoints are for ComplianceOfficers only; anyone else gets 404 (security.md §3).</summary>
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
