using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Documents.Contracts;
using Modules.Notifications.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Organizations;

/// <param name="Method"><c>Qes</c> (a QES-signed PDF) or <c>Scanned</c> (a hand-signed scan, confirmed through the official email).</param>
public sealed record PristupnicaUploadRequest(Guid DocumentId, string Method);

public sealed record PristupnicaDecisionRequest(string Decision, string? SignerName, Guid? MatchedRepresentativeId, Guid? PowerOfAttorneyDocumentId, string? Notes);

public sealed record OfficialEmailRequest(string Email, Guid? SourceDocumentId);

internal static class PristupnicaStatus
{
    public const string Generated = "Generated";

    public const string Uploaded = "Uploaded";

    public const string EmailConfirmed = "EmailConfirmed";

    public const string Accepted = "Accepted";

    public const string Rejected = "Rejected";
}

internal static class PristupnicaMethod
{
    public const string Qes = "Qes";

    public const string Scanned = "ScannedConfirmedByEmail";
}

/// <summary>
/// The pristupnica (ADR-020, task 007a requirements 3–5): generated from the organization's data, uploaded signed,
/// confirmed through the official email when it is a scan, and accepted or rejected by a Compliance Officer.
/// Accepting it makes the seller's single account Signatory.
/// </summary>
internal sealed class PristupnicaService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IAccessLog accessLog,
    IRequestContext request,
    IDocumentWriter documentWriter,
    Shared.Infrastructure.Outbox.IOutbox outbox,
    IDocumentQuery documents,
    IKybQuery kyb,
    IPristupnicaRenderer renderer,
    ConfirmationTokens confirmations,
    TimeProvider timeProvider)
{
    private static readonly string[] Open = [PristupnicaStatus.Generated, PristupnicaStatus.Uploaded, PristupnicaStatus.EmailConfirmed];

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary>
    /// <c>GET /organizations/{id}/pristupnica</c>: the current pristupnica. While none is uploaded, the PDF is rendered
    /// from the current data and stored again only when its content changed.
    /// </summary>
    public async Task<IResult> GetAsync(CancellationToken cancellationToken)
    {
        var organization = await orgs.Organizations.SingleAsync(cancellationToken);
        if (organization.OrgType != OrganizationType.Seller)
        {
            return Problems.ConflictState("Only seller organizations sign a pristupnica.");
        }

        if (organization.ClaimedAt is null)
        {
            return Problems.ConflictState("Prove the PIB with a SEF key first; the pristupnica follows the claim.");
        }

        var latest = await LatestAsync(organization.Id, cancellationToken);
        if (latest is not null && latest.Status != PristupnicaStatus.Generated && latest.Status != PristupnicaStatus.Rejected)
        {
            return Results.Ok(View(latest, organization));
        }

        var content = renderer.Render(await LinesAsync(organization, cancellationToken));
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        if (latest is { Status: PristupnicaStatus.Generated } && latest.GeneratedSha256 == sha256)
        {
            return Results.Ok(View(latest, organization));
        }

        var stored = await documentWriter.StoreAsync(organization.Id, DocumentPurpose.Pristupnica, $"pristupnica-{organization.Pib}.pdf", content, cancellationToken)
            ?? throw new InvalidOperationException("The generated pristupnica was not accepted by the document store.");
        var pristupnica = new Pristupnica
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = organization.Id,
            GeneratedDocumentId = stored.Id,
            GeneratedSha256 = sha256,
            Status = PristupnicaStatus.Generated,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        orgs.Pristupnice.Add(pristupnica);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Pristupnica.Generated", "Pristupnica", pristupnica.Id, organization.Id,
                    NewState: PristupnicaStatus.Generated, EvidenceDocumentIds: [stored.Id]), ct);
            },
            cancellationToken);
        return Results.Ok(View(pristupnica, organization));
    }

    /// <summary>
    /// <c>POST /organizations/{id}/pristupnica</c>: the signed copy. A scan sends a confirmation link to the official
    /// email; without a recorded email the link goes out when compliance records it.
    /// </summary>
    public async Task<IResult> UploadAsync(PristupnicaUploadRequest body, CancellationToken cancellationToken)
    {
        var method = body.Method switch { "Qes" => PristupnicaMethod.Qes, "Scanned" => PristupnicaMethod.Scanned, _ => null };
        if (method is null)
        {
            return Problems.Validation("method", "invalid_method", "Method must be Qes or Scanned.");
        }

        var organization = await orgs.Organizations.SingleAsync(cancellationToken);
        if (await LatestAsync(organization.Id, cancellationToken) is not { } pristupnica || !Open.Contains(pristupnica.Status))
        {
            return Problems.ConflictState("Download the pristupnica first (GET), then upload the signed copy.");
        }

        if (await documents.GetAsync(body.DocumentId, cancellationToken) is not { ScanStatus: ScanStatus.Clean } document
            || document.OwnerOrgId != organization.Id || document.Purpose != DocumentPurpose.Pristupnica || document.Id == pristupnica.GeneratedDocumentId)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "wrong_document", "Upload the signed pristupnica as a clean document with purpose Pristupnica.");
        }

        var previous = pristupnica.Status;
        pristupnica.SignedDocumentId = document.Id;
        pristupnica.Method = method;
        pristupnica.Status = PristupnicaStatus.Uploaded;
        EmailMessage? confirmation = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Pristupnica.Uploaded", "Pristupnica", pristupnica.Id, organization.Id,
                    PreviousState: previous, NewState: PristupnicaStatus.Uploaded, Reason: method, EvidenceDocumentIds: [document.Id]), ct);
                if (method == PristupnicaMethod.Scanned && organization.OfficialEmail is not null)
                {
                    confirmation = await confirmations.IssueAsync(organization, ConfirmationPurposes.Pristupnica, pristupnica.Id, "potvrda potpisane pristupnice", ct, pristupnica.SignedDocumentId?.ToString("N"));
                }
            },
            cancellationToken);
        if (confirmation is not null)
        {
            await confirmations.SendAsync(confirmation, cancellationToken);
        }

        return Results.Ok(View(pristupnica, organization));
    }

    /// <summary>The official email confirmed the scanned copy (a <c>Pristupnica</c> confirmation token); runs in the caller's transaction.</summary>
    public async Task<bool> ConfirmByEmailAsync(Guid pristupnicaId, string? fingerprint, CancellationToken cancellationToken)
    {
        // The link confirms the document it was sent for; a re-upload after it was sent voids it.
        if (await orgs.Pristupnice.SingleOrDefaultAsync(p => p.Id == pristupnicaId, cancellationToken) is not { Status: PristupnicaStatus.Uploaded, Method: PristupnicaMethod.Scanned } pristupnica
            || pristupnica.SignedDocumentId?.ToString("N") != fingerprint)
        {
            return false;
        }

        pristupnica.Status = PristupnicaStatus.EmailConfirmed;
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Pristupnica.EmailConfirmed", "Pristupnica", pristupnica.Id, pristupnica.OrgId,
            PreviousState: PristupnicaStatus.Uploaded, NewState: PristupnicaStatus.EmailConfirmed), cancellationToken);
        return true;
    }

    /// <summary><c>GET /admin/pristupnice</c>: pristupnice waiting for a decision, with the missing-email flag (letter-to-seat fallback).</summary>
    public async Task<IResult> QueueAsync(string? status, CancellationToken cancellationToken)
    {
        var statuses = status is null ? [PristupnicaStatus.Uploaded, PristupnicaStatus.EmailConfirmed] : new[] { status };
        var rows = await (from p in orgs.Pristupnice.AsNoTracking()
                          join o in orgs.Organizations.IgnoreQueryFilters().AsNoTracking() on p.OrgId equals o.Id
                          where statuses.Contains(p.Status)
                          orderby p.CreatedAt
                          select new { p, o })
            .Take(200)
            .ToListAsync(cancellationToken);
        var orgIds = rows.Select(r => r.o.Id).Distinct().ToList();
        var representatives = (await orgs.LegalRepresentatives.IgnoreQueryFilters().AsNoTracking()
                .Where(r => orgIds.Contains(r.OrgId))
                .Select(r => new { r.OrgId, r.Id, r.Name, r.Role })
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.OrgId);
        return Results.Ok(new
        {
            items = rows.Select(r => new
            {
                pristupnica = View(r.p, r.o),
                representatives = representatives[r.o.Id].Select(x => new { id = x.Id, name = x.Name, role = x.Role }),
            }),
        });
    }

    /// <summary>
    /// <c>GET /admin/pristupnice/{id}/content?which=generated|signed</c>: the compliance officer reads the document they
    /// decide on; the read is access-logged (audit.md §3).
    /// </summary>
    public async Task<IResult> ContentAsync(Guid id, string? which, HttpResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var pristupnica = await orgs.Pristupnice.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        var documentId = which switch { "signed" => pristupnica?.SignedDocumentId, "generated" or null => pristupnica?.GeneratedDocumentId, _ => null };
        if (pristupnica is null || documentId is not { } documentValue
            || await documents.GetAsync(documentValue, cancellationToken) is not { } document
            || await documents.ReadCleanAsync(documentValue, cancellationToken) is not { } content)
        {
            return NotFound();
        }

        await unitOfWork.ExecuteInTransactionAsync(
            ct => accessLog.RecordAsync(new AccessLogEntry("Document.Downloaded", "Document", document.Id, document.OwnerOrgId), ct),
            cancellationToken);
        response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(content, document.ContentType, document.FileName);
    }

    /// <summary>
    /// <c>POST /admin/pristupnice/{id}/decision</c> (step-up): Accepted needs a QES copy or an email-confirmed scan, the
    /// signer's name, and a matched APR representative or a power-of-attorney document (QES checked manually, LQ-32).
    /// </summary>
    public async Task<IResult> DecideAsync(Guid id, PristupnicaDecisionRequest body, CancellationToken cancellationToken)
    {
        if (await orgs.Pristupnice.SingleOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } pristupnica)
        {
            return NotFound();
        }

        var notes = body.Notes?.Trim();
        if (body.Decision is not (PristupnicaStatus.Accepted or PristupnicaStatus.Rejected) || notes?.Length > 1000
            || (body.Decision == PristupnicaStatus.Rejected && string.IsNullOrEmpty(notes)))
        {
            return Problems.Validation("decision", "invalid_decision", "Decide Accepted or Rejected; a rejection needs a reason in notes (at most 1000 characters).");
        }

        if (pristupnica.Status is not (PristupnicaStatus.Uploaded or PristupnicaStatus.EmailConfirmed))
        {
            return Problems.ConflictState($"The pristupnica is {pristupnica.Status}.");
        }

        var membership = await orgs.Memberships.IgnoreQueryFilters()
            .SingleAsync(m => m.OrgId == pristupnica.OrgId && m.Status == MembershipStatus.Active, cancellationToken);
        if (body.Decision == PristupnicaStatus.Accepted)
        {
            if (pristupnica.Method == PristupnicaMethod.Scanned && pristupnica.Status != PristupnicaStatus.EmailConfirmed)
            {
                return Problems.Error(StatusCodes.Status409Conflict, "email_confirmation_missing", "A scanned pristupnica needs the official-email confirmation first.");
            }

            var signer = body.SignerName?.Trim();
            if (string.IsNullOrEmpty(signer) || signer.Length > 200 || (body.MatchedRepresentativeId is null) == (body.PowerOfAttorneyDocumentId is null))
            {
                return Problems.Validation("signerName", "signer_required",
                    "Record the signer's name and either the matched APR representative or the power-of-attorney document.");
            }

            if (body.MatchedRepresentativeId is { } representativeId
                && !await orgs.LegalRepresentatives.IgnoreQueryFilters().AnyAsync(r => r.Id == representativeId && r.OrgId == pristupnica.OrgId, cancellationToken))
            {
                return Problems.Validation("matchedRepresentativeId", "unknown_representative", "The representative is not one of this organization's.");
            }

            if (body.PowerOfAttorneyDocumentId is { } poaId
                && (await documents.GetAsync(poaId, cancellationToken) is not { Purpose: DocumentPurpose.PowerOfAttorney, ScanStatus: ScanStatus.Clean } poa
                    || poa.OwnerOrgId != pristupnica.OrgId))
            {
                return Problems.Validation("powerOfAttorneyDocumentId", "wrong_document", "The power of attorney must be a clean PowerOfAttorney document of this organization.");
            }

            pristupnica.SignerName = signer;
            pristupnica.MatchedRepresentativeId = body.MatchedRepresentativeId;
            pristupnica.PowerOfAttorneyDocumentId = body.PowerOfAttorneyDocumentId;
            membership.IsSignatory = true;
            membership.LegalRepresentativeId = body.MatchedRepresentativeId;
            membership.PowerOfAttorneyDocumentId = body.PowerOfAttorneyDocumentId;
        }

        var previous = pristupnica.Status;
        pristupnica.Status = body.Decision;
        pristupnica.Notes = notes;
        pristupnica.DecidedBy = UserId;
        pristupnica.DecidedAt = timeProvider.GetUtcNow();
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry($"Pristupnica.{body.Decision}", "Pristupnica", pristupnica.Id, pristupnica.OrgId,
                        PreviousState: previous, NewState: body.Decision, Reason: notes,
                        Changes: new Dictionary<string, string?> { ["method"] = pristupnica.Method, ["signerName"] = pristupnica.SignerName }), ct);
                    if (body.Decision == PristupnicaStatus.Accepted)
                    {
                        await audit.WriteAsync(new AuditEntry("Member.SignatoryChanged", "Organization", pristupnica.OrgId, pristupnica.OrgId,
                            Changes: new Dictionary<string, string?> { ["isSignatory"] = "False → True" }, Reason: "pristupnica_accepted",
                            Related: [new AuditReference("Pristupnica", pristupnica.Id)]), ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }

        return Results.Ok(new { id = pristupnica.Id, status = pristupnica.Status });
    }

    /// <summary>
    /// <c>POST /admin/organizations/{id}/official-email</c> (step-up): the email from the APR snapshot. A scanned
    /// pristupnica that waited for it gets its confirmation link now.
    /// </summary>
    public async Task<IResult> RecordOfficialEmailAsync(Guid organizationId, OfficialEmailRequest body, CancellationToken cancellationToken)
    {
        var address = body.Email?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > 200 || !address.Contains('@', StringComparison.Ordinal))
        {
            return Problems.Validation("email", "invalid_email", "A valid email address is required.");
        }

        if (await orgs.Organizations.IgnoreQueryFilters().SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken) is not { } organization)
        {
            return NotFound();
        }

        // The official email is the root of trust for confirmations: it is recorded only with its APR source document.
        if (body.SourceDocumentId is not { } sourceId || (await documents.GetAsync(sourceId, cancellationToken))?.OwnerOrgId != organization.Id)
        {
            return Problems.Validation("sourceDocumentId", "wrong_document", "Attach the APR excerpt of this organization as the source document.");
        }

        var previous = organization.OfficialEmail;
        organization.OfficialEmail = address;
        organization.OfficialEmailSourceDocumentId = body.SourceDocumentId;
        organization.OfficialEmailRecordedBy = UserId;
        organization.OfficialEmailRecordedAt = timeProvider.GetUtcNow();
        var waiting = await orgs.Pristupnice.Where(p => p.OrgId == organization.Id && p.Status == PristupnicaStatus.Uploaded && p.Method == PristupnicaMethod.Scanned)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        EmailMessage? confirmation = null;
        EmailMessage? oldAddressNotice = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Organization.OfficialEmailRecorded", "Organization", organization.Id, organization.Id,
                    Changes: new Dictionary<string, string?> { ["officialEmail"] = $"{Masking.Email(previous)} → {Masking.Email(address)}" },
                    EvidenceDocumentIds: body.SourceDocumentId is { } source ? [source] : null), ct);
                if (previous is not null && !string.Equals(previous, address, StringComparison.OrdinalIgnoreCase))
                {
                    // A changed address voids the links sent to the old one; the old address and the Owners are told.
                    foreach (var open in await orgs.ConfirmationTokens.Where(t => t.OrgId == organization.Id && t.UsedAt == null).ToListAsync(ct))
                    {
                        open.UsedAt = timeProvider.GetUtcNow();
                    }

                    await orgs.SaveChangesAsync(ct);
                    await outbox.AddAsync(new OrganizationNotificationRequested(organization.Id, NotificationTypes.Security, $"/organizations/{organization.Id}", null), ct);
                    oldAddressNotice = new EmailMessage(previous, "Cesio: službeni email firme je promenjen",
                        "Službeni email vaše firme na platformi Cesio je promenjen na osnovu izvoda iz APR-a. Ako to nije tačno, odmah se javite podršci Cesio.");
                }

                if (waiting is not null)
                {
                    confirmation = await confirmations.IssueAsync(organization, ConfirmationPurposes.Pristupnica, waiting.Id, "potvrda potpisane pristupnice", ct, waiting.SignedDocumentId?.ToString("N"));
                }
            },
            cancellationToken);
        if (confirmation is not null)
        {
            await confirmations.SendAsync(confirmation, cancellationToken);
        }

        if (oldAddressNotice is not null)
        {
            await confirmations.SendAsync(oldAddressNotice, cancellationToken);
        }

        return Results.Ok(new { id = organization.Id, officialEmailRecorded = true, confirmationSent = confirmation is not null });
    }

    /// <summary>
    /// <c>POST /admin/organizations/{id}/evidence</c>: an officer stores an APR excerpt or another source document as the
    /// organization's KYB evidence (e.g. the source of the official email).
    /// </summary>
    public async Task<IResult> UploadEvidenceAsync(Guid organizationId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!await orgs.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Id == organizationId, cancellationToken))
        {
            return NotFound();
        }

        if (file is null || file.Length is 0 or > 10 * 1024 * 1024)
        {
            return Problems.Validation("file", "file_required", "Upload the document (PDF or image, at most 10 MB).");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return await documentWriter.StoreAsync(organizationId, DocumentPurpose.KybEvidence, Path.GetFileName(file.FileName), content, cancellationToken) is { } stored
            ? Results.Created($"/api/v1/documents/{stored.Id}", new { documentId = stored.Id })
            : Problems.Error(StatusCodes.Status422UnprocessableEntity, "unsupported_file_type", "The document must be a PDF or an image.");
    }

    private Task<Pristupnica?> LatestAsync(Guid organizationId, CancellationToken cancellationToken) =>
        orgs.Pristupnice.Where(p => p.OrgId == organizationId).OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>The pristupnica text (ADR-020); the legal wording comes from the lawyer (LQ-32, LQ-51), this is the data part.</summary>
    private async Task<List<string>> LinesAsync(Organization organization, CancellationToken cancellationToken)
    {
        var representatives = await orgs.LegalRepresentatives.Where(r => r.OrgId == organization.Id).OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var owners = await kyb.BeneficialOwnersAsync(organization.Id, cancellationToken);
        var terms = await orgs.TermsAcceptances.Where(t => t.OrgId == organization.Id).OrderByDescending(t => t.AcceptedAt).Select(t => t.TermsVersion).FirstOrDefaultAsync(cancellationToken);
        var lines = new List<string>
        {
            "PRISTUPNICA PLATFORMI CESIO",
            "NACRT - tekst ceka pravnu verifikaciju",
            string.Empty,
            $"Datum: {DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}",
            $"Firma: {organization.LegalName ?? "-"}",
            $"PIB: {organization.Pib}   MB: {organization.Mb ?? "-"}",
            $"Pravna forma: {organization.LegalForm ?? "-"}",
            $"Sediste: {organization.RegisteredAddress ?? "-"}",
            string.Empty,
            "Zakonski zastupnici (APR):",
        };
        lines.AddRange(representatives.Count == 0 ? ["  -"] : representatives.Select(r => $"  {r.Name}, {r.Role}"));
        lines.Add(string.Empty);
        lines.Add("Stvarni vlasnici:");
        lines.AddRange(owners.Count == 0 ? ["  -"] : owners.Select(o => $"  {o.Name}{(o.OwnershipPercent is { } p ? $", {p.ToString("0.##", CultureInfo.InvariantCulture)} %" : string.Empty)}"));
        lines.Add(string.Empty);
        lines.Add($"Prihvacena verzija Uslova koriscenja: {terms ?? "-"}");
        lines.Add($"Vlasnik racuna za isplatu: {organization.LegalName ?? organization.Pib}");
        lines.Add("Saglasnost za koriscenje SEF API kljuca: tekst saglasnosti ceka pravnu verifikaciju");
        lines.Add($"Saglasnost za deljenje KYB paketa sa faktorom: {(await kyb.HasSharingConsentAsync(organization.Id, cancellationToken) ? "da" : "ne")}");
        lines.Add(string.Empty);
        lines.Add("Mesto i datum potpisa: ______________________________");
        lines.Add("Ime i svojstvo potpisnika (zastupnik ili punomocnik): ______________________________");
        lines.Add("Punomocje (ako potpisuje punomocnik): ______________________________");
        lines.Add(string.Empty);
        lines.Add("Potpis: ______________________________");
        return lines;
    }

    private static object View(Pristupnica p, Organization o) => new
    {
        id = p.Id,
        organizationId = p.OrgId,
        pib = o.Pib,
        legalName = o.LegalName,
        status = p.Status,
        method = p.Method,
        generatedDocumentId = p.GeneratedDocumentId,
        generatedSha256 = p.GeneratedSha256,
        signedDocumentId = p.SignedDocumentId,
        signerName = p.SignerName,
        notes = p.Notes,
        decidedAt = p.DecidedAt,
        officialEmailMissing = o.OfficialEmail is null,
        version = p.Version,
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}
