using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals.Contracts;
using Modules.Fees.Contracts;
using Modules.Fees.Persistence;
using Modules.Identity.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Fees;

/// <summary>Composition entry point of the Fees module; the hosts call it to register the module's services.</summary>
public static class FeesModule
{
    public static IServiceCollection AddFeesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<FeesDbContext>(FeesDbContext.SchemaName, migrationOrder: 7);
        services.AddSingleton<IFeeCalculator, FeeCalculator>();
        services.AddScoped<FeeScheduleService>();
        services.AddScoped<IFeeScheduleQuery>(provider => provider.GetRequiredService<FeeScheduleService>());
        services.AddScoped<FeeChargeService>();
        services.AddIntegrationEventConsumer<DealFunded, FeeChargeConsumer>(FeeChargeConsumer.ConsumerName);
        return services;
    }

    /// <summary>Admin fee schedules (task 017a) and charges (task 018): Finance staff only, anyone else gets 404; the payer org's charges and statements.</summary>
    public static IEndpointRouteBuilder MapFeeEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/fee-schedules").RequireAuthorization().AddEndpointFilter(RequireFinance);
        admin.MapGet("", (FeeScheduleService service, CancellationToken ct) => service.ListAsync(ct));
        admin.MapPost("", (CreateFeeScheduleRequest body, FeeScheduleService service, CancellationToken ct) => service.CreateAsync(body, ct))
            .RequireIdempotency();
        admin.MapPost("/{version:int}/approve", (int version, FeeScheduleService service, CancellationToken ct) => service.ApproveAsync(version, ct))
            .RequireIdempotency().RequireStepUp();

        var charges = app.MapGroup("/api/v1/admin/fees").RequireAuthorization().AddEndpointFilter(RequireFinance);
        charges.MapGet("/charges", ([AsParameters] ChargeFilter filter, FeeChargeService service, CancellationToken ct) => service.AdminListAsync(filter, ct));
        charges.MapPost("/charges/{id:guid}/invoiced", (Guid id, InvoicedRequest body, FeeChargeService service, CancellationToken ct) =>
            service.MarkInvoicedAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        charges.MapPost("/charges/{id:guid}/paid", (Guid id, PaidRequest body, FeeChargeService service, CancellationToken ct) =>
            service.MarkPaidAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        charges.MapPost("/charges/{id:guid}/reverse", (Guid id, ReverseRequest body, FeeChargeService service, CancellationToken ct) =>
            service.ReverseAsync(id, body, ct)).RequireIdempotency().RequireStepUp();
        charges.MapPost("/charges/{id:guid}/approve-reversal", (Guid id, FeeChargeService service, CancellationToken ct) =>
            service.ApproveReversalAsync(id, ct)).RequireIdempotency().RequireStepUp();
        charges.MapGet("/export", (string? period, FeeChargeService service, CancellationToken ct) => service.ExportAsync(period, ct));

        var payer = app.MapGroup("/api/v1/fees").RequireAuthorization().RequireOrganization();
        payer.MapGet("/charges", (FeeChargeService service, CancellationToken ct) => service.MineAsync(ct));
        payer.MapGet("/statements/{period}", (string period, FeeChargeService service, CancellationToken ct) => service.StatementAsync(period, ct));
        return app;
    }

    private static async ValueTask<object?> RequireFinance(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && await roles.HasRoleAsync(userId, PlatformRoles.Finance, http.RequestAborted)
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
