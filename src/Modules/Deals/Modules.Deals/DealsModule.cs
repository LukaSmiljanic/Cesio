using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Deals.Persistence;
using Modules.Documents.Contracts;
using Modules.Identity.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Jobs;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Deals;

/// <summary>Composition entry point of the Deals module; the hosts call it to register the module's services.</summary>
public static class DealsModule
{
    /// <summary>Environments where the fake signature validator may run (like the fake SEF client).</summary>
    private static readonly string[] FakeValidatorEnvironments = ["Development", "local", "dev"];

    public static IServiceCollection AddDealsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<DealsDbContext>(DealsDbContext.SchemaName, migrationOrder: 8);
        services.AddScoped<DealService>();
        services.AddScoped<Contracts.IDealQuery, DealQuery>();
        services.AddScoped<DealStepService>();
        services.AddScoped<DealJobs>();
        services.AddScoped<DisputeService>();
        services.AddScoped<Contracts.IDealStats, DealStats>();
        services.AddIntegrationEventConsumer<Modules.Receivables.Contracts.ReceivableSefStatusChanged, DealJobs>(DealJobs.SefConsumerName);
        services.AddIntegrationEventConsumer<Modules.Compliance.Contracts.FactorAuthorizationRevoked, DealJobs>(DealJobs.RevokedConsumerName);
        services.AddIntegrationEventConsumer<Modules.Compliance.Contracts.FactorAuthorizationExpired, DealJobs>(DealJobs.ExpiredConsumerName);
        services.AddJob("deals-signature-deadline", TimeSpan.FromMinutes(15), (provider, ct) => provider.GetRequiredService<DealJobs>().SignatureDeadlinesAsync(ct));
        services.AddJob("deals-funding-follow-up", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<DealJobs>().FundingFollowUpAsync(ct));
        services.AddJob("deals-outcome-overdue", TimeSpan.FromDays(1), (provider, ct) => provider.GetRequiredService<DealJobs>().OutcomeOverdueAsync(ct));
        services.AddScoped<TrustStoreService>();
        services.AddScoped<ICefGateway, CefNotOperationalGateway>();
        services.AddScoped<IDocumentAccessPolicy, DealDocumentPolicy>();
        services.AddSingleton<ISignatureValidator>(provider =>
        {
            var fake = provider.GetRequiredService<IConfiguration>()["Signatures:Validator"] == "Fake";
            var environment = provider.GetRequiredService<IHostEnvironment>().EnvironmentName;
            return !fake
                ? new UnconfiguredSignatureValidator()
                : FakeValidatorEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)
                    ? new FakeSignatureValidator()
                    : throw new InvalidOperationException($"The fake signature validator must not run in '{environment}'.");
        });
        return services;
    }

    /// <summary>
    /// Offer acceptance and the deal reads of the two parties (task 014); the steps before signing (task 015); the
    /// signed contract and the trust store (task 016).
    /// </summary>
    public static IEndpointRouteBuilder MapDealEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization().RequireOrganization();
        api.MapPost("/offers/{id:guid}/accept", (Guid id, AcceptOfferRequest body, HttpContext http, DealService service, CancellationToken ct) =>
            service.AcceptAsync(id, body, IfMatch(http), StepUpAt(http), ct))
            .RequireIdempotency().RequireStepUp();
        api.MapGet("/deals", (DealService service, CancellationToken ct) => service.ListAsync(ct));
        api.MapGet("/deals/export", (DealService service, CancellationToken ct) => service.ExportAsync(ct));
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
        api.MapPost("/deals/{id:guid}/signed-contract", (Guid id, ContractRequest body, DealStepService service, CancellationToken ct) =>
            service.UploadSignedContractAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        api.MapGet("/deals/{id:guid}/funding-accounts", (Guid id, DealStepService service, CancellationToken ct) => service.FundingAccountsAsync(id, ct));
        api.MapPost("/deals/{id:guid}/cef-registration", (Guid id, CefRegistrationRequest body, DealStepService service, CancellationToken ct) =>
            service.RecordCefRegistrationAsync(id, body, ct)).RequireIdempotency();
        api.MapPost("/deals/{id:guid}/debtor-notification", (Guid id, DebtorNotificationRequest body, DealStepService service, CancellationToken ct) =>
            service.RecordDebtorNotificationAsync(id, body, ct)).RequireIdempotency();
        api.MapPost("/deals/{id:guid}/funding-report", (Guid id, FundingReportRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.ReportFundingAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();
        api.MapPost("/deals/{id:guid}/funding-confirmation", (Guid id, FundingConfirmationRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.ConfirmFundingAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();
        api.MapPost("/deals/{id:guid}/outcome", (Guid id, OutcomeRequest body, HttpContext http, DealStepService service, CancellationToken ct) =>
            service.RecordOutcomeAsync(id, body, StepUpAt(http), ct)).RequireIdempotency().RequireStepUp();

        api.MapGet("/deals/{id:guid}/disputes", (Guid id, DisputeService service, CancellationToken ct) => service.ForDealAsync(id, ct));
        api.MapPost("/deals/{id:guid}/disputes", (Guid id, OpenDisputeRequest body, DisputeService service, CancellationToken ct) => service.OpenAsync(id, body, ct)).RequireIdempotency();
        api.MapGet("/disputes/{id:guid}", (Guid id, DisputeService service, CancellationToken ct) => service.GetAsync(id, staff: false, ct));
        api.MapPost("/disputes/{id:guid}/comments", (Guid id, DisputeCommentRequest body, DisputeService service, CancellationToken ct) =>
            service.CommentAsync(id, body, staff: false, ct)).RequireIdempotency();
        api.MapPost("/disputes/{id:guid}/withdraw", (Guid id, DisputeService service, CancellationToken ct) => service.WithdrawAsync(id, ct)).RequireIdempotency();

        var disputes = app.MapGroup("/api/v1/admin/disputes").RequireAuthorization().AddEndpointFilter(RequireOperationsOrCompliance);
        disputes.MapGet("", (string? status, DisputeService service, CancellationToken ct) => service.QueueAsync(status, ct));
        disputes.MapGet("/{id:guid}", (Guid id, DisputeService service, CancellationToken ct) => service.GetAsync(id, staff: true, ct));
        disputes.MapPost("/{id:guid}/comments", (Guid id, DisputeCommentRequest body, DisputeService service, CancellationToken ct) =>
            service.CommentAsync(id, body, staff: true, ct)).RequireIdempotency();
        disputes.MapPost("/{id:guid}/assign", (Guid id, DisputeService service, CancellationToken ct) => service.AssignAsync(id, ct)).RequireIdempotency();
        disputes.MapPost("/{id:guid}/resolve", (Guid id, ResolveDisputeRequest body, DisputeService service, CancellationToken ct) =>
            service.ResolveAsync(id, body, ct)).RequireIdempotency().RequireStepUp();

        var staff = app.MapGroup("/api/v1/admin/deals").RequireAuthorization().AddEndpointFilter(RequireOperationsOrCompliance);
        staff.MapPost("/{id:guid}/terminate", (Guid id, TerminateDealRequest body, DealStepService service, CancellationToken ct) =>
            service.TerminateAsync(id, body, ct)).RequireIdempotency().RequireStepUp();

        var admin = app.MapGroup("/api/v1/admin/trusted-certificates").RequireAuthorization().AddEndpointFilter(RequirePlatformAdmin);
        admin.MapGet("", (TrustStoreService service, CancellationToken ct) => service.ListAsync(ct));
        admin.MapPost("", (AddTrustedCertificateRequest body, TrustStoreService service, CancellationToken ct) => service.AddAsync(body, ct))
            .RequireIdempotency().RequireStepUp();
        admin.MapPost("/{id:guid}/deactivate", (Guid id, TrustStoreService service, CancellationToken ct) => service.DeactivateAsync(id, ct))
            .RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>The session's step-up time; <c>RequireStepUp</c> already checked that it is recent.</summary>
    private static DateTimeOffset StepUpAt(HttpContext http) => (DateTimeOffset)http.Items[CesioClaims.MfaStepUpAtItem]!;

    /// <summary>The listing version from <c>If-Match: "3"</c>, if sent (api.md §1).</summary>
    private static int? IfMatch(HttpContext http) =>
        int.TryParse(http.Request.Headers.IfMatch.ToString().Trim('"', ' '), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;

    /// <summary>Terminating a signed deal is for Operations or compliance staff; anyone else gets 404 (security.md §3).</summary>
    private static async ValueTask<object?> RequireOperationsOrCompliance(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && (await roles.HasRoleAsync(userId, PlatformRoles.Operations, http.RequestAborted) || await roles.HasRoleAsync(userId, PlatformRoles.ComplianceOfficer, http.RequestAborted))
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }

    /// <summary>The trust store is for platform administrators only; anyone else gets 404 (security.md §3).</summary>
    private static async ValueTask<object?> RequirePlatformAdmin(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && await roles.HasRoleAsync(userId, PlatformRoles.PlatformAdmin, http.RequestAborted)
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
