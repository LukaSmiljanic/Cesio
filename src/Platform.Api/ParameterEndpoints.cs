using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// The parameters the modules read, and whether a change needs a second person (fee terms and amount limits). Only
    /// these keys can be changed, spelled exactly so; SQL compares keys case-insensitively, so a differently cased key
    /// must never reach the store as a new, unchecked parameter. The fee share is not here: the fee schedule (task 017a,
    /// four-eyes) is its only source.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (bool Money, ValueKind Kind)> Known = new Dictionary<string, (bool, ValueKind)>(StringComparer.Ordinal)
    {
        ["fees.paymentTermsDays"] = (true, ValueKind.PositiveInteger),
        ["receivables.minFaceValue"] = (true, ValueKind.PositiveNumber),
        ["receivables.maxFaceValue"] = (true, ValueKind.PositiveNumber),
        ["receivables.autoApproveMaxFaceValue"] = (true, ValueKind.PositiveNumber),
        ["receivables.minDaysAtListing"] = (false, ValueKind.PositiveInteger),
        ["receivables.maxTenorDays"] = (false, ValueKind.PositiveInteger),
        ["receivables.warnOnNewDebtor"] = (false, ValueKind.Boolean),
        ["marketplace.maxListingDays"] = (false, ValueKind.PositiveInteger),
        ["marketplace.maxAcceptanceWindowHours"] = (false, ValueKind.PositiveInteger),
        ["deals.signatureDeadlineBusinessDays"] = (false, ValueKind.PositiveInteger),
        ["deals.fundingConfirmationAlertBusinessDays"] = (false, ValueKind.PositiveInteger),
        ["cef.operationalFrom"] = (false, ValueKind.Date),
        ["cef.blockFundingUntilRegistered"] = (false, ValueKind.Boolean),
        ["screening.threshold"] = (false, ValueKind.Share),
        ["confirmations.tokenHours"] = (false, ValueKind.PositiveInteger),
        ["security.stepUpBlockHoursAfterMfaReset"] = (false, ValueKind.PositiveInteger),
        ["orgs.maxSellerMembers"] = (false, ValueKind.PositiveInteger),
        ["orgs.draftClaimDays"] = (false, ValueKind.PositiveInteger),
    };

    public enum ValueKind
    {
        /// <summary>A decimal above 0 and at most 1, such as the fee share of the discount.</summary>
        Share,
        PositiveNumber,
        PositiveInteger,
        Boolean,
        /// <summary>An ISO date or date-time string.</summary>
        Date,
    }

    public static bool IsMoneyRelated(string key) => !Known.TryGetValue(key, out var known) || known.Money;

    /// <summary>Whether the value has the type and range the key needs; decimals are read as decimal, never as floating point.</summary>
    public static bool IsValid(ValueKind kind, JsonElement value) => kind switch
    {
        ValueKind.Share => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var share) && share > 0m && share <= 1m,
        ValueKind.PositiveNumber => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number > 0m,
        ValueKind.PositiveInteger => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer) && integer > 0,
        ValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        ValueKind.Date => value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _),
        _ => false,
    };

    private static async Task<IResult> ChangeAsync(string key, ParameterChangeRequest body, IRequestContext request, IBusinessParameterStore store,
        UnitOfWork unitOfWork, IAuditWriter audit, CancellationToken ct)
    {
        if (!Known.TryGetValue(key, out var known) || !IsValid(known.Kind, body.Value))
        {
            return Problems("validation_failed", StatusCodes.Status400BadRequest, "Give a known parameter key and a value of its type and range.");
        }

        var value = body.Value.GetRawText();
        var userId = request.UserId!.Value;
        // The audit keeps the old and the new value of every change.
        var previous = (await store.CurrentAsync(ct)).SingleOrDefault(p => p.Key == key);
        if (IsMoneyRelated(key))
        {
            ParameterProposalInfo? proposal = null;
            await unitOfWork.ExecuteInTransactionAsync(
                async c =>
                {
                    proposal = await store.ProposeAsync(key, value, userId, c);
                    await audit.WriteAsync(new AuditEntry("BusinessParameter.Proposed", "BusinessParameter", proposal.Id,
                        Changes: new Dictionary<string, string?> { ["key"] = key, ["previousValue"] = previous?.Value, ["value"] = value }), c);
                },
                ct);
            return Results.Accepted(value: proposal);
        }

        ParameterVersion? version = null;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async c =>
                {
                    version = await store.AddVersionAsync(key, value, userId, c);
                    await audit.WriteAsync(new AuditEntry("BusinessParameter.Changed", "BusinessParameter", Guid.Empty,
                        Changes: new Dictionary<string, string?> { ["key"] = key, ["version"] = version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), ["previousValue"] = previous?.Value, ["value"] = value }), c);
                },
                ct);
        }
        catch (DbUpdateException)
        {
            // Another change of the same key took this version number first.
            return Problems("conflict_state", StatusCodes.Status409Conflict, "The parameter changed at the same time; try again.");
        }

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
                if (version is null)
                {
                    return;
                }

                await audit.WriteAsync(new AuditEntry("BusinessParameter.Approved", "BusinessParameter", id,
                    Changes: new Dictionary<string, string?> { ["key"] = proposal.Key, ["value"] = proposal.Value }), c);
            },
            ct);
        return version is null
            ? Problems("conflict_state", StatusCodes.Status409Conflict, "The proposal was approved at the same time.")
            : Results.Ok(version);
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
