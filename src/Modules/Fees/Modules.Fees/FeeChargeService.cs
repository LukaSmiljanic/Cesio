using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Notifications.Contracts;
using Modules.Fees.Persistence;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Fees;

public sealed record InvoicedRequest(string? ExternalInvoiceRef);

public sealed record PaidRequest(DateOnly PaidOn, string? Reference);

public sealed record ReverseRequest(string? Reason);

public sealed record ChargeFilter(string? Period, Guid? PayerOrgId, string? Status);

/// <summary>One currency's statement total; reversals are subtracted.</summary>
public sealed record StatementTotal(Money Net, Money Vat, Money Total);

/// <summary>
/// Fee charges and statements (task 018): the payer sees its own charges and monthly statements; Finance marks charges
/// invoiced and paid, reverses them with a second person's approval, and exports a month as CSV.
/// </summary>
internal sealed class FeeChargeService(FeesDbContext fees, UnitOfWork unitOfWork, IAuditWriter audit,
    IOutbox outbox, IRequestContext request, TimeProvider timeProvider)
{
    private Guid OrganizationId => request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> MineAsync(CancellationToken cancellationToken) =>
        Results.Ok((await Effective(fees.FeeCharges.AsNoTracking().Where(c => c.PayerOrgId == OrganizationId))
            .OrderByDescending(c => c.FundedAt).Take(500).ToListAsync(cancellationToken)).Select(View));

    /// <summary>The payer's statement for one month: charges of deals funded in it and reversals that took effect in it.</summary>
    public async Task<IResult> StatementAsync(string period, CancellationToken cancellationToken)
    {
        if (Period(period) is not { } range)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The period is yyyy-mm.");
        }

        var rows = await StatementRowsAsync(fees.FeeCharges.AsNoTracking().Where(c => c.PayerOrgId == OrganizationId), range, cancellationToken);
        return Results.Ok(new { period, lines = rows.Select(View), totals = Totals(rows) });
    }

    public async Task<IResult> AdminListAsync(ChargeFilter filter, CancellationToken cancellationToken)
    {
        var query = fees.FeeCharges.AsNoTracking();
        if (filter.Period is not null)
        {
            if (Period(filter.Period) is not { } range)
            {
                return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The period is yyyy-mm.");
            }

            query = query.Where(c => (c.Kind == FeeCharge.ChargeKind && c.FundedAt >= range.From && c.FundedAt < range.To)
                || (c.Kind == FeeCharge.ReversalKind && c.CreatedAt >= range.From && c.CreatedAt < range.To));
        }

        if (filter.PayerOrgId is { } payer)
        {
            query = query.Where(c => c.PayerOrgId == payer);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        return Results.Ok((await query.OrderByDescending(c => c.CreatedAt).Take(1000).ToListAsync(cancellationToken)).Select(View));
    }

    public Task<IResult> MarkInvoicedAsync(Guid id, InvoicedRequest body, CancellationToken cancellationToken) =>
        ChangeChargeAsync(id, "Due", "Invoiced", "FeeCharge.Invoiced", charge =>
        {
            var reference = body.ExternalInvoiceRef?.Trim();
            if (string.IsNullOrEmpty(reference) || reference.Length > 200)
            {
                return "Give the external (SEF) invoice reference.";
            }

            charge.ExternalInvoiceRef = reference;
            return null;
        }, cancellationToken);

    public Task<IResult> MarkPaidAsync(Guid id, PaidRequest body, CancellationToken cancellationToken) =>
        ChangeChargeAsync(id, "Invoiced", "Paid", "FeeCharge.Paid", charge =>
        {
            var reference = body.Reference?.Trim();
            if (string.IsNullOrEmpty(reference) || reference.Length > 200 || body.PaidOn > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            {
                return "Give the payment reference and a payment date that is not in the future.";
            }

            fees.FeePayments.Add(new FeePayment
            {
                Id = SequentialGuid.NewGuid(),
                ChargeId = charge.Id,
                PaidOn = body.PaidOn,
                Reference = reference,
                RecordedBy = UserId,
                RecordedAt = timeProvider.GetUtcNow(),
            });
            return null;
        }, cancellationToken);

    /// <summary>A Finance user proposes reversing a charge (with a reason); it takes effect when a second one approves.</summary>
    public async Task<IResult> ReverseAsync(Guid id, ReverseRequest body, CancellationToken cancellationToken)
    {
        if (await fees.FeeCharges.SingleOrDefaultAsync(c => c.Id == id && c.Kind == FeeCharge.ChargeKind, cancellationToken) is not { } charge)
        {
            return NotFound();
        }

        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 400)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the reason (at most 400 characters).");
        }

        if (await fees.FeeCharges.AnyAsync(c => c.ReversesId == id && c.Status != "Withdrawn", cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The charge already has a reversal.");
        }

        var reversal = new FeeCharge
        {
            Id = SequentialGuid.NewGuid(),
            DealId = charge.DealId,
            PayerOrgId = charge.PayerOrgId,
            PayerRole = charge.PayerRole,
            Kind = FeeCharge.ReversalKind,
            Amount = charge.Amount,
            VatAmount = charge.VatAmount,
            VatPending = charge.VatPending,
            FeeScheduleVersion = charge.FeeScheduleVersion,
            Status = "PendingApproval",
            ReversesId = charge.Id,
            Reason = reason,
            DealReference = charge.DealReference,
            InvoiceNumber = charge.InvoiceNumber,
            FaceValueAmount = charge.FaceValueAmount,
            FundedAt = charge.FundedAt,
            CreatedBy = UserId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        fees.FeeCharges.Add(reversal);
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await fees.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("FeeCharge.ReversalProposed", "FeeCharge", reversal.Id, charge.PayerOrgId, NewState: "PendingApproval",
                        Reason: reason, Related: [new AuditReference("FeeCharge", charge.Id)]), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The charge already has a reversal.");
        }

        return Results.Created($"/api/v1/admin/fees/charges/{reversal.Id}", View(reversal));
    }

    public async Task<IResult> ApproveReversalAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await fees.FeeCharges.SingleOrDefaultAsync(c => c.Id == id && c.Kind == FeeCharge.ReversalKind, cancellationToken) is not { } reversal)
        {
            return NotFound();
        }

        if (reversal.Status != "PendingApproval")
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The reversal is already effective.");
        }

        if (reversal.CreatedBy == UserId)
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "four_eyes_required", "A second person must approve the reversal.");
        }

        reversal.Status = "Effective";
        reversal.ApprovedBy = UserId;
        reversal.ApprovedAt = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await fees.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("FeeCharge.Reversed", "FeeCharge", reversal.Id, reversal.PayerOrgId,
                        PreviousState: "PendingApproval", NewState: "Effective", Related: [new AuditReference("FeeCharge", reversal.ReversesId!.Value)]), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another officer approved the reversal first.");
        }

        return Results.Ok(View(reversal));
    }

    /// <summary>
    /// <c>POST /admin/fees/charges/{id}/withdraw-reversal</c> (task 021 follow-up): Finance withdraws a reversal that is
    /// still pending, e.g. proposed by mistake; the charge can then be reversed again.
    /// </summary>
    public async Task<IResult> WithdrawReversalAsync(Guid id, ReverseRequest body, CancellationToken cancellationToken)
    {
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 400)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the reason (at most 400 characters).");
        }

        if (await fees.FeeCharges.SingleOrDefaultAsync(c => c.Id == id && c.Kind == FeeCharge.ReversalKind, cancellationToken) is not { } reversal)
        {
            return NotFound();
        }

        if (reversal.Status != "PendingApproval")
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"The reversal is {reversal.Status}.");
        }

        reversal.Status = "Withdrawn";
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await fees.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("FeeCharge.ReversalWithdrawn", "FeeCharge", reversal.Id, reversal.PayerOrgId,
                        PreviousState: "PendingApproval", NewState: "Withdrawn", Reason: reason, Related: [new AuditReference("FeeCharge", reversal.ReversesId!.Value)]), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The reversal was changed in the meantime.");
        }

        return Results.Ok(View(reversal));
    }

    /// <summary>The month's charges minus effective reversals as CSV for the accounting system (invariant decimals, UTF-8 BOM).</summary>
    public async Task<IResult> ExportAsync(string? period, CancellationToken cancellationToken)
    {
        if (Period(period) is not { } range)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "The period is yyyy-mm.");
        }

        var rows = await StatementRowsAsync(fees.FeeCharges.AsNoTracking(), range, cancellationToken);
        var csv = new StringBuilder("kind;payer_org_id;payer_role;deal_reference;invoice_number;face_value;fee_net;vat;total;currency;funded_at;status\r\n");
        foreach (var row in rows)
        {
            var sign = row.Kind == FeeCharge.ReversalKind ? -1m : 1m;
            var net = sign * row.Amount.Round().Amount;
            var vat = sign * decimal.Round(row.VatAmount, 2, MidpointRounding.AwayFromZero);
            csv.Append(string.Create(CultureInfo.InvariantCulture,
                $"{row.Kind};{row.PayerOrgId};{row.PayerRole};{row.DealReference};{Csv(row.InvoiceNumber)};{row.FaceValueAmount:0.00};{net:0.00};{vat:0.00};{net + vat:0.00};{row.Amount.Currency.Code};{row.FundedAt:yyyy-MM-dd};{row.Status}\r\n"));
        }

        await unitOfWork.ExecuteInTransactionAsync(
            ct => audit.WriteAsync(new AuditEntry("FeeExport.Downloaded", "FeeExport", Guid.Empty,
                Changes: new Dictionary<string, string?> { ["period"] = period, ["rows"] = rows.Count.ToString(CultureInfo.InvariantCulture) }), ct),
            cancellationToken);
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", $"cesio-fees-{period}.csv");
    }

    /// <summary>
    /// A text cell: quoted when it holds a separator, quote or line break, and a leading <c>= + - @</c>, tab or CR (the
    /// invoice number comes from the seller's SEF data) is neutralised with an apostrophe so a spreadsheet never runs it.
    /// </summary>
    internal static string Csv(string value)
    {
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return safe.Contains(';', StringComparison.Ordinal) || safe.Contains('"', StringComparison.Ordinal) || safe.Contains('\n', StringComparison.Ordinal)
            ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : safe;
    }

    private async Task<IResult> ChangeChargeAsync(Guid id, string from, string to, string action, Func<FeeCharge, string?> apply, CancellationToken cancellationToken)
    {
        if (await fees.FeeCharges.SingleOrDefaultAsync(c => c.Id == id && c.Kind == FeeCharge.ChargeKind, cancellationToken) is not { } charge)
        {
            return NotFound();
        }

        if (await fees.FeeCharges.AnyAsync(c => c.ReversesId == id && c.Status != "Withdrawn", cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "charge_reversed", "The charge has a reversal; it is not invoiced or collected.");
        }

        if (charge.Status != from)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", $"Only a {from} charge can become {to}.");
        }

        if (apply(charge) is { } problem)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", problem);
        }

        charge.Status = to;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await fees.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "FeeCharge", id, charge.PayerOrgId, PreviousState: from, NewState: to,
                        Changes: new Dictionary<string, string?> { ["externalInvoiceRef"] = charge.ExternalInvoiceRef }), ct);
                    if (to == "Invoiced")
                    {
                        // "Fee statement issued" (product-spec §8): the payer learns that its charge was invoiced.
                        await outbox.AddAsync(new OrganizationNotificationRequested(charge.PayerOrgId, NotificationTypes.FeeStatement, "/fees", null), ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "Another request changed the charge first.");
        }

        return Results.Ok(View(charge));
    }

    private static async Task<List<FeeCharge>> StatementRowsAsync(IQueryable<FeeCharge> query, (DateTimeOffset From, DateTimeOffset To) range, CancellationToken cancellationToken) =>
        await Effective(query)
            .Where(c => (c.Kind == FeeCharge.ChargeKind && c.FundedAt >= range.From && c.FundedAt < range.To)
                || (c.Kind == FeeCharge.ReversalKind && c.ApprovedAt >= range.From && c.ApprovedAt < range.To))
            .OrderBy(c => c.FundedAt).ThenBy(c => c.Kind)
            .ToListAsync(cancellationToken);

    /// <summary>Charges and effective reversals; a reversal still waiting for its second approver does not count.</summary>
    private static IQueryable<FeeCharge> Effective(IQueryable<FeeCharge> query) =>
        query.Where(c => c.Kind == FeeCharge.ChargeKind || c.Status == "Effective");

    /// <summary>Per currency: net, VAT and total, reversals subtracted (Money sums never mix currencies).</summary>
    private static List<StatementTotal> Totals(List<FeeCharge> rows) =>
        rows.GroupBy(r => r.Amount.Currency.Code).Select(group =>
        {
            var net = group.Sum(r => (r.Kind == FeeCharge.ReversalKind ? -1m : 1m) * r.Amount.Round().Amount);
            var vat = group.Sum(r => (r.Kind == FeeCharge.ReversalKind ? -1m : 1m) * decimal.Round(r.VatAmount, 2, MidpointRounding.AwayFromZero));
            var currency = Currency.FromCode(group.Key);
            return new StatementTotal(new Money(net, currency).Round(), new Money(vat, currency).Round(), new Money(net + vat, currency).Round());
        }).ToList();

    private static (DateTimeOffset From, DateTimeOffset To)? Period(string? period) =>
        DateOnly.TryParseExact($"{period}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? (new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new DateTimeOffset(first.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
            : null;

    private static object View(FeeCharge charge) => new
    {
        id = charge.Id,
        kind = charge.Kind,
        dealId = charge.DealId,
        dealReference = charge.DealReference,
        invoiceNumber = charge.InvoiceNumber,
        payerOrgId = charge.PayerOrgId,
        payerRole = charge.PayerRole,
        amount = charge.Amount.Round(),
        vat = new Money(charge.VatAmount, charge.Amount.Currency).Round(),
        vatPending = charge.VatPending,
        status = charge.Status,
        dueAt = charge.DueAt,
        externalInvoiceRef = charge.ExternalInvoiceRef,
        reversesId = charge.ReversesId,
        reason = charge.Reason,
        fundedAt = charge.FundedAt,
        feeScheduleVersion = charge.FeeScheduleVersion,
    };

    private static IResult NotFound() => Problems.Error(StatusCodes.Status404NotFound, "not_found", "The charge does not exist.");
}
