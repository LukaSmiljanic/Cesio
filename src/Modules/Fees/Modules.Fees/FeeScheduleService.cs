using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Fees.Contracts;
using Modules.Fees.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Fees;

/// <param name="Definition">A <see cref="FeeScheduleDefinition"/> as JSON, with the currency as its ISO code.</param>
public sealed record CreateFeeScheduleRequest(JsonElement Definition, DateTimeOffset EffectiveFrom);

/// <summary>Fee schedule versions (task 017a): Finance creates one, a second Finance user approves it (four-eyes).</summary>
internal sealed class FeeScheduleService(
    FeesDbContext fees,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    TimeProvider timeProvider) : IFeeScheduleQuery
{
    internal static readonly JsonSerializerOptions DefinitionJson =
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(), new CurrencyCodeConverter() } }.AddKernelConverters();

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<FeeSchedule> CurrentAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var row = await fees.FeeSchedules.AsNoTracking()
            .Where(s => s.ApprovedBy != null && s.EffectiveFrom <= at)
            .OrderByDescending(s => s.EffectiveFrom).ThenByDescending(s => s.Version)
            .FirstAsync(cancellationToken);
        return ToSchedule(row);
    }

    public async Task<FeeSchedule?> GetAsync(int version, CancellationToken cancellationToken) =>
        await fees.FeeSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Version == version, cancellationToken) is { } row ? ToSchedule(row) : null;

    public async Task<IResult> ListAsync(CancellationToken cancellationToken) =>
        Results.Ok((await fees.FeeSchedules.AsNoTracking().OrderByDescending(s => s.Version).ToListAsync(cancellationToken)).Select(View));

    public async Task<IResult> CreateAsync(CreateFeeScheduleRequest body, CancellationToken cancellationToken)
    {
        FeeScheduleDefinition? definition;
        try
        {
            definition = body.Definition.ValueKind == JsonValueKind.Object ? body.Definition.Deserialize<FeeScheduleDefinition>(DefinitionJson) : null;
        }
        catch (JsonException)
        {
            definition = null;
        }

        if (Invalid(definition) is { } problem)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", problem);
        }

        if (body.EffectiveFrom < timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "effectiveFrom cannot be in the past.");
        }

        var row = new FeeScheduleRow
        {
            Version = await fees.FeeSchedules.MaxAsync(s => s.Version, cancellationToken) + 1,
            Definition = JsonSerializer.Serialize(definition, DefinitionJson),
            EffectiveFrom = body.EffectiveFrom,
            CreatedBy = UserId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        fees.FeeSchedules.Add(row);
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await fees.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("FeeSchedule.Created", "FeeSchedule", VersionId(row.Version),
                        NewState: "PendingApproval", Changes: new Dictionary<string, string?> { ["definition"] = row.Definition, ["effectiveFrom"] = row.EffectiveFrom.ToString("O") }), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another schedule version was created at the same time; try again.");
        }

        return Results.Created($"/api/v1/admin/fee-schedules/{row.Version}", View(row));
    }

    /// <summary>The second person approves; the version takes effect at its effective_from, which must not have passed.</summary>
    public async Task<IResult> ApproveAsync(int version, CancellationToken cancellationToken)
    {
        if (await fees.FeeSchedules.SingleOrDefaultAsync(s => s.Version == version, cancellationToken) is not { } row)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The fee schedule does not exist.");
        }

        if (row.ApprovedBy is not null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The fee schedule is already approved.");
        }

        if (row.CreatedBy == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second person must approve the fee schedule.");
        }

        if (row.EffectiveFrom < timeProvider.GetUtcNow())
        {
            return Problems.Error(StatusCodes.Status409Conflict, "effective_date_passed", "The effective date has passed; create a new version.");
        }

        row.ApprovedBy = UserId;
        row.ApprovedAt = timeProvider.GetUtcNow();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await fees.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("FeeSchedule.Approved", "FeeSchedule", VersionId(version),
                    PreviousState: "PendingApproval", NewState: "Approved", Changes: new Dictionary<string, string?> { ["definition"] = row.Definition }), ct);
            },
            cancellationToken);
        return Results.Ok(View(row));
    }

    /// <summary>Audit entities have GUID ids; a schedule version maps to a stable one.</summary>
    internal static Guid VersionId(int version) => new(version, 0, 0, [0, 0, 0, 0, 0, 0, 0xFE, 0xE5]);

    private static string? Invalid(FeeScheduleDefinition? definition)
    {
        if (definition is null)
        {
            return "Give the schedule definition.";
        }

        if (definition.Basis != FeeBasis.Discount || definition.Currency != Currency.Rsd)
        {
            return "The MVP supports the Discount basis in RSD only (ADR-007, ADR-009).";
        }

        if (definition.Share is <= 0 or > 1)
        {
            return "share must be above 0 and at most 1.";
        }

        if (definition.Payer == FeePayer.Split ? definition.SellerShareOfFee is not (> 0 and < 1) : definition.SellerShareOfFee is not null)
        {
            return "sellerShareOfFee is required (between 0 and 1) for a Split payer and not allowed otherwise.";
        }

        if ((definition.MinimumFee is { } min && (min.Currency != definition.Currency || min.Amount < 0))
            || (definition.MaximumFee is { } max && (max.Currency != definition.Currency || max.Amount <= 0))
            || (definition.MinimumFee is { } low && definition.MaximumFee is { } high && low.Currency == high.Currency && low > high))
        {
            return "minimumFee and maximumFee are in the schedule's currency, not negative, and the minimum is not above the maximum.";
        }

        return null;
    }

    private static FeeSchedule ToSchedule(FeeScheduleRow row) =>
        new(row.Version, JsonSerializer.Deserialize<FeeScheduleDefinition>(row.Definition, DefinitionJson)!, row.EffectiveFrom);

    private static object View(FeeScheduleRow row) => new
    {
        version = row.Version,
        definition = JsonSerializer.Deserialize<JsonElement>(row.Definition),
        effectiveFrom = row.EffectiveFrom,
        createdBy = row.CreatedBy,
        createdAt = row.CreatedAt,
        approvedBy = row.ApprovedBy,
        approvedAt = row.ApprovedAt,
        status = row.ApprovedBy is null ? "PendingApproval" : "Approved",
    };
}

internal static class Problems
{
    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
