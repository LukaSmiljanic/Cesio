using System.Text.Json;
using Modules.Audit.Contracts;
using Modules.Identity.Contracts;
using Shared.Infrastructure.Idempotency;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;

internal sealed record ParameterChangeRequest(JsonElement Value);

/// <summary>
/// The business parameters editor (task 022): PlatformAdmin and Finance see and change them (step-up, audited, a new
/// version each time); a money-related parameter is proposed by one and approved by another person (four-eyes).
/// </summary>
internal static class ParameterEndpoints
{
    public static IEndpointRouteBuilder MapParameterEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/parameters").RequireAuthorization().AddEndpointFilter(RequireParameterAdmin);
        admin.MapGet("", async (IBusinessParameterStore store, CancellationToken ct) =>
            Results.Ok(new { current = await store.CurrentAsync(ct), pending = await store.PendingProposalsAsync(ct) }));
        admin.MapGet("/{key}", async (string key, IBusinessParameterStore store, CancellationToken ct) => Results.Ok(await store.HistoryAsync(key, ct)));
        admin.MapPut("/{key}", ChangeAsync).RequireIdempotency().RequireStepUp();
        admin.MapPost("/proposals/{id:guid}/approve", ApproveAsync).RequireIdempotency().RequireStepUp();
        return app;
    }

    /// <summary>Money-related keys (amount limits and fee terms) need a second person.</summary>
    public static bool IsMoneyRelated(string key) =>
        key.StartsWith("fees.", StringComparison.Ordinal) || key.EndsWith("FaceValue", StringComparison.Ordinal) || key.EndsWith("Amount", StringComparison.Ordinal);

    private static async Task<IResult> ChangeAsync(string key, ParameterChangeRequest body, IRequestContext request, IBusinessParameterStore store,
        UnitOfWork unitOfWork, IAuditWriter audit, CancellationToken ct)
    {
        if (key.Length is 0 or > 200 || !key.Contains('.', StringComparison.Ordinal) || body.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Problems("validation_failed", StatusCodes.Status400BadRequest, "Give a key like module.name and a JSON value.");
        }

        var value = body.Value.GetRawText();
        var userId = request.UserId!.Value;
        if (IsMoneyRelated(key))
        {
            ParameterProposalInfo? proposal = null;
            await unitOfWork.ExecuteInTransactionAsync(
                async c =>
                {
                    proposal = await store.ProposeAsync(key, value, userId, c);
                    await audit.WriteAsync(new AuditEntry("BusinessParameter.Proposed", "BusinessParameter", proposal.Id,
                        Changes: new Dictionary<string, string?> { ["key"] = key, ["value"] = value }), c);
                },
                ct);
            return Results.Accepted(value: proposal);
        }

        ParameterVersion? version = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async c =>
            {
                version = await store.AddVersionAsync(key, value, userId, c);
                await audit.WriteAsync(new AuditEntry("BusinessParameter.Changed", "BusinessParameter", Guid.Empty,
                    Changes: new Dictionary<string, string?> { ["key"] = key, ["version"] = version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), ["value"] = value }), c);
            },
            ct);
        return Results.Ok(version);
    }

    private static async Task<IResult> ApproveAsync(Guid id, IRequestContext request, IBusinessParameterStore store, UnitOfWork unitOfWork, IAuditWriter audit, CancellationToken ct)
    {
        var userId = request.UserId!.Value;
        if ((await store.PendingProposalsAsync(ct)).SingleOrDefault(p => p.Id == id) is not { } proposal)
        {
            return Problems("not_found", StatusCodes.Status404NotFound, "No pending proposal.");
        }

        if (proposal.ProposedBy == userId)
        {
            return Problems("four_eyes_required", StatusCodes.Status403Forbidden, "A second person must approve a money-related parameter.");
        }

        ParameterVersion? version = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async c =>
            {
                version = await store.ApproveAsync(id, userId, c);
                await audit.WriteAsync(new AuditEntry("BusinessParameter.Approved", "BusinessParameter", id,
                    Changes: new Dictionary<string, string?> { ["key"] = proposal.Key, ["value"] = proposal.Value }), c);
            },
            ct);
        return Results.Ok(version);
    }

    private static async ValueTask<object?> RequireParameterAdmin(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && (await roles.HasRoleAsync(userId, PlatformRoles.PlatformAdmin, http.RequestAborted) || await roles.HasRoleAsync(userId, PlatformRoles.Finance, http.RequestAborted))
                ? await next(context)
                : Problems("not_found", StatusCodes.Status404NotFound, null);
    }

    private static IResult Problems(string errorCode, int status, string? detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
