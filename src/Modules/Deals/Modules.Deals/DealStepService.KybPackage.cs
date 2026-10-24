using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Deals.Contracts;

namespace Modules.Deals;

/// <summary>The seller's KYB package for the factor's own CDD (task 015 requirement 1, ADR-015).</summary>
internal sealed partial class DealStepService
{
    private static readonly JsonSerializerOptions PackageJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// A ZIP generated on demand for the factor of the deal, only with the seller's KYB-sharing consent: the KYB
    /// summary (JSON, the screening summary but not the raw hits) and the receivable's documents. The download is
    /// audited and logged.
    /// </summary>
    public async Task<IResult> KybPackageAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await PartyDealAsync(id, cancellationToken) is not { } deal || deal.FactorOrgId != OrganizationId)
        {
            return NotFound();
        }

        if (deal.State is DealState.Cancelled or DealState.Terminated)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The deal has ended.");
        }

        // INV-A2: personal data of the beneficial owners only to a factor that is still authorized and not on hold.
        if (!await authorizations.IsFactorAuthorizedAsync(deal.FactorOrgId, cancellationToken) || await holds.HasActiveHoldAsync(HoldSubject.Deal, deal.Id, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status403Forbidden, "factor_not_authorized", "The KYB package is available only to an authorized factor without a hold.");
        }

        if (!await kyb.HasSharingConsentAsync(deal.SellerOrgId, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "kyb_consent_missing", "The seller has not consented to sharing its KYB package.");
        }

        var seller = await organizations.GetAsync(deal.SellerOrgId, cancellationToken);
        var (riskRating, screeningSummary) = await kyb.SummaryAsync(deal.SellerOrgId, cancellationToken);
        var receivable = await receivableMarket.GetAsync(deal.ReceivableId, cancellationToken);
        var summary = new
        {
            generatedAt = timeProvider.GetUtcNow(),
            deal = deal.Reference,
            seller = new { legalName = seller?.LegalName, pib = seller?.Pib, kybStatus = seller?.Status.ToString(), riskRating },
            legalRepresentatives = await organizations.AuthorizedSignerNamesAsync(deal.SellerOrgId, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime), cancellationToken),
            beneficialOwners = await kyb.BeneficialOwnersAsync(deal.SellerOrgId, cancellationToken),
            screening = screeningSummary,
        };

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var entry = await zip.CreateEntry("kyb-summary.json").OpenAsync(cancellationToken))
            {
                await JsonSerializer.SerializeAsync(entry, summary, PackageJson, cancellationToken);
            }

            foreach (var documentId in receivable is null ? [] : receivable.EvidenceDocumentIds.Prepend(receivable.UblDocumentId))
            {
                if (await documents.GetAsync(documentId, cancellationToken) is { } info && await documents.ReadCleanAsync(documentId, cancellationToken) is { } content)
                {
                    await using var entry = await zip.CreateEntry($"documents/{info.Id:N}-{info.FileName}").OpenAsync(cancellationToken);
                    await entry.WriteAsync(content, cancellationToken);
                }
            }
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await accessLog.RecordAsync(new AccessLogEntry("Deal.KybPackageDownloaded", "Deal", id, OrganizationId), ct);
                await audit.WriteAsync(new AuditEntry("Deal.KybPackageDownloaded", "Deal", id, OrganizationId, Related: [new AuditReference("Organization", deal.SellerOrgId)]), ct);
            },
            cancellationToken);
        return Results.File(buffer.ToArray(), "application/zip", $"kyb-{deal.Reference}.zip");
    }
}
