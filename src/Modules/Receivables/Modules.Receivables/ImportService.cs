using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Integrations.Sef;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Audit.Contracts;
using Modules.Documents.Contracts;
using Modules.Organizations.Contracts;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Infrastructure.Secrets;
using Shared.Kernel;

namespace Modules.Receivables;

/// <param name="SefInvoiceIds">At most 50 SEF invoice IDs; or a date range instead.</param>
public sealed record ImportFromSefRequest(IReadOnlyList<string>? SefInvoiceIds, DateOnly? IssueDateFrom, DateOnly? IssueDateTo);

public sealed record ImportResult(string SefInvoiceId, string Status, Guid? ReceivableId = null, string? Reason = null);

/// <summary>
/// <c>POST /receivables/import-from-sef</c> (api.md §3.1, task 010): each SEF invoice becomes an immutable Invoice
/// with its UBL stored as a document and a Draft receivable. Problems are reported per item, not as a failed batch.
/// </summary>
internal sealed partial class ImportService(
    ReceivablesDbContext receivables,
    ISefClient sef,
    ISefConnections connections,
    ISecretStore secrets,
    IOrganizationQuery organizations,
    IDocumentWriter documents,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    TimeProvider timeProvider,
    ILogger<ImportService> logger)
{
    public const int MaxBatch = 50;

    public async Task<IResult> ImportAsync(ImportFromSefRequest body, CancellationToken cancellationToken)
    {
        var orgId = request.OrganizationId ?? throw new InvalidOperationException("An organization context is required.");
        var organization = await organizations.GetAsync(orgId, cancellationToken);
        if (organization is not { Type: OrganizationType.Seller })
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "forbidden", "Only seller organizations import invoices.");
        }

        var connection = await connections.GetAsync(orgId, cancellationToken);
        var apiKey = connection is { Status: SefConnectionStatus.Connected } ? await secrets.GetAsync(connection.SecretName, cancellationToken) : null;
        if (apiKey is null)
        {
            return SefConnectionInvalid();
        }

        var results = new List<ImportResult>();
        try
        {
            if (await RequestedIdsAsync(body, apiKey, cancellationToken) is not { } ids)
            {
                return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed",
                    $"Send 1 to {MaxBatch} numeric sefInvoiceIds, or issueDateFrom ≤ issueDateTo of at most 31 days.");
            }

            foreach (var id in ids.Distinct())
            {
                results.Add(await ImportOneAsync(orgId, organization.Pib, apiKey, id, cancellationToken));
            }

            return Results.Ok(new { results });
        }
        catch (SefUnauthorizedException)
        {
            await connections.MarkInvalidAsync(orgId, cancellationToken);
            // Items imported before the key stopped working stay imported (409 is final) and are listed.
            return Results.Problem(detail: "SEF rejected the stored API key. Connect a valid key.", statusCode: StatusCodes.Status409Conflict,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "sef_connection_invalid", ["results"] = results });
        }
        catch (SefUnavailableException)
        {
            return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "SEF is not available. Try again later.");
        }
    }

    public async Task<IResult> ListAsync(CancellationToken cancellationToken) =>
        Results.Ok((await Views().OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(cancellationToken)).Select(ForApi));

    public async Task<IResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Views().SingleOrDefaultAsync(r => r.Id == id, cancellationToken) is { } view
            ? Results.Ok(ForApi(view))
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });

    /// <summary>Amounts are stored as decimal(19,4) and shown with the currency's minor units ("1200000.00", api.md §1).</summary>
    private static ReceivableView ForApi(ReceivableView view) =>
        view with { FaceValue = view.FaceValue.Round(), Invoice = view.Invoice with { Gross = view.Invoice.Gross.Round() } };

    private IQueryable<ReceivableView> Views() =>
        from r in receivables.Receivables.AsNoTracking()
        join i in receivables.Invoices on r.InvoiceId equals i.Id
        join d in receivables.Debtors on r.DebtorId equals d.Id
        select new ReceivableView(r.Id, r.State, r.FaceValue, r.MaturityDate, r.CreatedAt, r.Version,
            new InvoiceView(i.Id, i.SefInvoiceId, i.InvoiceNumber, i.IssueDate, i.DueDate, new Money(i.GrossAmount, i.Currency), i.SefStatus, i.UblDocumentId, i.UblSha256),
            new DebtorView(d.Id, d.Pib, d.LegalName));

    private async Task<IReadOnlyList<long>?> RequestedIdsAsync(ImportFromSefRequest body, string apiKey, CancellationToken cancellationToken)
    {
        if (body.SefInvoiceIds is { Count: > 0 and <= MaxBatch } raw)
        {
            var ids = new List<long>();
            foreach (var value in raw)
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                {
                    return null;
                }

                ids.Add(id);
            }

            return ids;
        }

        if (body.SefInvoiceIds is null && body.IssueDateFrom is { } from && body.IssueDateTo is { } to && from <= to && to.DayNumber - from.DayNumber <= 31)
        {
            var listed = await sef.ListSalesInvoiceIdsAsync(apiKey, from, to, cancellationToken);
            return [.. listed.Order().Take(MaxBatch)];
        }

        return null;
    }

    private async Task<ImportResult> ImportOneAsync(Guid orgId, string orgPib, string apiKey, long id, CancellationToken cancellationToken)
    {
        var sefId = id.ToString(CultureInfo.InvariantCulture);
        // SEF first: only invoices this seller's key can see are looked up, so the answer never reveals what other
        // organizations imported.
        if (await sef.GetSalesInvoiceAsync(apiKey, id, cancellationToken) is not { } sefInvoice)
        {
            return new ImportResult(sefId, "NotFound");
        }

        if (await ExistingAsync(sefId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var ubl = await sef.GetSalesInvoiceUblAsync(apiKey, id, cancellationToken);
        UblInvoice invoice;
        try
        {
            invoice = UblParser.Parse(ubl);
        }
        catch (UblFormatException)
        {
            return new ImportResult(sefId, "Unsupported", Reason: "malformed_ubl");
        }

        if (Unsupported(invoice, orgPib) is { } rejection)
        {
            LogRejected(logger, sefId, rejection.Reason);
            return new ImportResult(sefId, rejection.Status, Reason: rejection.Reason);
        }

        var document = await documents.StoreAsync(orgId, DocumentPurpose.InvoiceUbl, $"sef-{sefId}.xml", ubl, cancellationToken);
        if (document is null)
        {
            return new ImportResult(sefId, "Unsupported", Reason: "ubl_not_storable");
        }

        // A parallel import may win a unique index: the same invoice (→ AlreadyImported), or a new debtor shared with
        // another invoice (→ one retry, the debtor now exists). A lost race can leave its UBL document unused.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await CreateAsync(orgId, sefId, sefInvoice, invoice, document, cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                receivables.ChangeTracker.Clear();
                if (await ExistingAsync(sefId, cancellationToken) is { } winner)
                {
                    return winner;
                }

                if (attempt > 0)
                {
                    return new ImportResult(sefId, "AlreadyImported", Reason: "duplicate_invoice_number");
                }
            }
        }
    }

    /// <summary>The task 010 business rules: issuer, RSD only (ADR-009), a B2B debtor with a PIB, consistent totals, a due date.</summary>
    private static (string Status, string Reason)? Unsupported(UblInvoice invoice, string orgPib)
    {
        if (invoice.Supplier.Pib != orgPib)
        {
            return ("NotIssuer", "supplier_pib_differs");
        }

        if (invoice.IsCreditNote)
        {
            return ("Unsupported", "credit_note");
        }

        if (invoice.Currency != Currency.Rsd.Code)
        {
            return ("Unsupported", "currency_not_rsd");
        }

        if (invoice.Buyer.Pib is null || !Pib.TryParse(invoice.Buyer.Pib, out _))
        {
            return ("Unsupported", "debtor_without_pib");
        }

        if (invoice.NetAmount < 0 || invoice.VatAmount < 0 || invoice.GrossAmount <= 0
            || invoice.NetAmount + invoice.VatAmount != invoice.GrossAmount)
        {
            return ("Unsupported", "totals_inconsistent");
        }

        // A deducted prepayment means the debtor owes less than the gross amount; how to value such a receivable is
        // not decided, so it is not imported (rather than overstating the face value).
        if (invoice.PayableAmount is { } payable && payable != invoice.GrossAmount)
        {
            return ("Unsupported", "payable_differs_from_gross");
        }

        return invoice.DueDate is { } due && due >= invoice.IssueDate ? null : ("Unsupported", "due_date_missing");
    }

    private async Task<ImportResult> CreateAsync(
        Guid orgId, string sefId, SefSalesInvoice sefInvoice, UblInvoice ubl, DocumentInfo document, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var debtor = await receivables.Debtors.SingleOrDefaultAsync(d => d.Pib == ubl.Buyer.Pib, cancellationToken);
        if (debtor is null)
        {
            debtor = new Debtor { Id = SequentialGuid.NewGuid(), Pib = ubl.Buyer.Pib!, Mb = ubl.Buyer.Mb, LegalName = ubl.Buyer.Name ?? ubl.Buyer.Pib!, UpdatedAt = now };
            receivables.Debtors.Add(debtor);
        }

        var invoice = new Invoice
        {
            Id = SequentialGuid.NewGuid(),
            SellerOrgId = orgId,
            DebtorId = debtor.Id,
            SefInvoiceId = sefId,
            InvoiceNumber = ubl.Number,
            IssueDate = ubl.IssueDate,
            DueDate = ubl.DueDate!.Value,
            Currency = Currency.Rsd,
            NetAmount = ubl.NetAmount,
            VatAmount = ubl.VatAmount,
            GrossAmount = ubl.GrossAmount,
            UblDocumentId = document.Id,
            UblSha256 = document.Sha256,
            SefStatus = sefInvoice.Status,
            SefStatusCheckedAt = now,
            ImportedAt = now,
        };
        // MVP: one receivable over the full invoice; no credit notes are known at import (faceValue = gross).
        var receivable = new Receivable
        {
            Id = SequentialGuid.NewGuid(),
            InvoiceId = invoice.Id,
            SellerOrgId = orgId,
            DebtorId = debtor.Id,
            FaceValue = new Money(ubl.GrossAmount, Currency.Rsd),
            MaturityDate = invoice.DueDate,
            State = ReceivableState.Draft,
            Fingerprint = Fingerprint(debtor.Pib, ubl.GrossAmount, invoice.Currency, invoice.IssueDate, invoice.DueDate),
            CreatedAt = now,
        };
        receivables.Invoices.Add(invoice);
        receivables.Receivables.Add(receivable);
        receivables.SefStatusHistory.Add(new SefStatusObservation
        {
            Id = SequentialGuid.NewGuid(),
            InvoiceId = invoice.Id,
            Status = sefInvoice.Status,
            ObservedAt = now,
            Raw = JsonSerializer.Serialize(sefInvoice),
        });

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await receivables.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Invoice.Imported", "Invoice", invoice.Id, orgId,
                    Changes: new Dictionary<string, string?> { ["sefInvoiceId"] = sefId, ["ublSha256"] = document.Sha256, ["gross"] = new Money(ubl.GrossAmount, Currency.Rsd).ToString() },
                    EvidenceDocumentIds: [document.Id]), ct);
                await audit.WriteAsync(new AuditEntry("Receivable.Created", "Receivable", receivable.Id, orgId,
                    NewState: nameof(ReceivableState.Draft), Related: [new AuditReference("Invoice", invoice.Id)]), ct);
            },
            cancellationToken);
        return new ImportResult(sefId, "Imported", receivable.Id);
    }

    /// <summary>
    /// An invoice already imported (by anyone: the SEF ID is unique). For the same seller the live receivable is
    /// returned; another seller's import is reported as NotFound, so nothing about it is revealed.
    /// </summary>
    private async Task<ImportResult?> ExistingAsync(string sefId, CancellationToken cancellationToken)
    {
        var invoice = await receivables.Invoices.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(i => i.SefInvoiceId == sefId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.SellerOrgId != request.OrganizationId)
        {
            return new ImportResult(sefId, "NotFound");
        }

        var live = await receivables.Receivables.AsNoTracking()
            .Where(r => r.InvoiceId == invoice.Id && r.State != ReceivableState.Rejected && r.State != ReceivableState.Cancelled)
            .Select(r => (Guid?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return new ImportResult(sefId, "AlreadyImported", live);
    }

    private static string Fingerprint(string debtorPib, decimal gross, Currency currency, DateOnly issue, DateOnly due) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{debtorPib}|{gross:0.0000}|{currency.Code}|{issue:yyyy-MM-dd}|{due:yyyy-MM-dd}"))));

    /// <summary>The SEF ID and the reason only: no parties or amounts in logs (security.md §8).</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "SEF invoice {SefInvoiceId} not imported: {Reason}")]
    private static partial void LogRejected(ILogger logger, string sefInvoiceId, string reason);

    private static IResult SefConnectionInvalid() =>
        Problems.Error(StatusCodes.Status409Conflict, "sef_connection_invalid", "Connect a valid SEF API key first.");
}

public sealed record ReceivableView(
    Guid Id, ReceivableState State, Money FaceValue, DateOnly MaturityDate, DateTimeOffset CreatedAt, int Version, InvoiceView Invoice, DebtorView Debtor);

public sealed record InvoiceView(
    Guid Id, string SefInvoiceId, string Number, DateOnly IssueDate, DateOnly DueDate, Money Gross, string SefStatus, Guid UblDocumentId, string UblSha256);

public sealed record DebtorView(Guid Id, string Pib, string LegalName);

internal static class Problems
{
    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
