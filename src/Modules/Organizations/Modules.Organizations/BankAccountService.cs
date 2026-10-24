using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Documents.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Organizations;

public sealed record BankAccountRequest(string? AccountNumber, string? Currency);

public sealed record BankAccountVerification(Guid? EvidenceDocumentId, Guid? SupplementDocumentId, bool HolderMatchesPib);

public sealed record BankAccountReason(string? Reason);

public sealed record BankCodeRequest(string? Name);

internal static class BankAccountStatus
{
    public const string ConfirmationPending = "ConfirmationPending";

    public const string Pending = "Pending";

    public const string Verified = "Verified";

    public const string Rejected = "Rejected";

    public const string Deactivated = "Deactivated";
}

/// <summary>
/// Payout accounts (task 009, security.md §2, INV-F2): the Owner adds one (Pending; after the first Verified account an
/// addition is first confirmed through the official email), compliance verifies it against the NBS account register
/// snapshot, and it becomes usable for funding after the cool-down. The same number in another organization is
/// flagged on both records and blocks verification until compliance clears it.
/// </summary>
internal sealed class BankAccountService(
    OrgsDbContext orgs,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IRequestContext request,
    IDocumentQuery documents,
    IDocumentWriter documentWriter,
    IBusinessParameters parameters,
    ConfirmationTokens confirmations,
    TimeProvider timeProvider) : IBankAccountQuery
{
    public const string CoolDownHoursParameter = "bankAccounts.coolDownHours";

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary><c>GET /organizations/{id}/bank-accounts</c>: the owning organization sees the full numbers.</summary>
    public async Task<IResult> ListAsync(CancellationToken cancellationToken) =>
        Results.Ok((await orgs.BankAccounts.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken)).Select(a => View(a, full: true, staff: false)));

    /// <summary><c>POST /organizations/{id}/bank-accounts</c> (step-up).</summary>
    public async Task<IResult> AddAsync(BankAccountRequest body, CancellationToken cancellationToken)
    {
        if (!SerbianBankAccountNumber.TryParse(body.AccountNumber, out var number))
        {
            return Problems.Validation("accountNumber", "invalid_account_number", "The account number is not a valid Serbian account number (bank code, account, control digits).");
        }

        if ((body.Currency ?? "RSD") != Currency.Rsd.Code)
        {
            return Problems.Validation("currency", "unsupported_currency", "Payout accounts are in RSD.");
        }

        var organization = await orgs.Organizations.SingleAsync(cancellationToken);
        // Any account ever verified (also a deactivated one): deactivating the only account must not skip the email confirmation.
        var hasVerified = await orgs.BankAccounts.AnyAsync(a => a.VerifiedAt != null, cancellationToken);
        if (hasVerified && organization.OfficialEmail is null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "official_email_missing",
                "A further payout account is confirmed through the company's official email from APR, which is not recorded yet.");
        }

        if (await orgs.BankAccounts.AnyAsync(a => a.AccountNumber == number.Value && a.Status != BankAccountStatus.Deactivated && a.Status != BankAccountStatus.Rejected, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "already_exists", "This account is already registered.");
        }

        var bankName = await orgs.BankCodes.AsNoTracking().Where(c => c.Code == number.BankCode).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken)
            ?? $"Nepoznata banka (šifra {number.BankCode})";
        // Only the new record is flagged: another organization's account is neither blocked nor revealed by this one.
        var duplicates = await orgs.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.AccountNumber == number.Value && a.OrgId != organization.Id && a.Status != BankAccountStatus.Deactivated && a.Status != BankAccountStatus.Rejected)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        var account = new BankAccount
        {
            Id = SequentialGuid.NewGuid(),
            OrgId = organization.Id,
            AccountNumber = number.Value,
            Currency = Currency.Rsd.Code,
            BankName = bankName,
            Status = hasVerified ? BankAccountStatus.ConfirmationPending : BankAccountStatus.Pending,
            DuplicateFlag = duplicates.Count > 0,
            CreatedBy = UserId,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        orgs.BankAccounts.Add(account);
        EmailMessage? confirmation = null;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("BankAccount.Added", "BankAccount", account.Id, organization.Id, NewState: account.Status,
                        Changes: new Dictionary<string, string?> { ["accountNumber"] = number.ToString(), ["bank"] = bankName }), ct);
                    if (duplicates.Count > 0)
                    {
                        // security.md §2 monitoring: the same account across organizations.
                        await audit.WriteAsync(new AuditEntry("Compliance.Alert", "BankAccount", account.Id, organization.Id, Reason: "account_registered_by_another_organization",
                            Related: duplicates.Select(d => new AuditReference("BankAccount", d)).ToList()), ct);
                    }

                    await outbox.AddAsync(new BankAccountAdded(organization.Id, account.Id), ct);
                    if (hasVerified)
                    {
                        confirmation = await confirmations.IssueAsync(organization, ConfirmationPurposes.PayoutAccountChange, account.Id,
                            $"dodavanje računa za isplatu {number}", ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Problems.Error(StatusCodes.Status409Conflict, "already_exists", "This account is already registered.");
        }

        if (confirmation is not null)
        {
            await confirmations.SendAsync(confirmation, cancellationToken);
        }

        return Results.Created($"/api/v1/organizations/{organization.Id}/bank-accounts/{account.Id}", View(account, full: true, staff: false));
    }

    /// <summary><c>POST /organizations/{id}/bank-accounts/{accountId}/deactivate</c> (step-up); reactivation is a new account and verification.</summary>
    public async Task<IResult> DeactivateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await orgs.BankAccounts.SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        if (account.Status is BankAccountStatus.Deactivated or BankAccountStatus.Rejected)
        {
            return Problems.ConflictState($"The account is {account.Status}.");
        }

        var previous = account.Status;
        account.Status = BankAccountStatus.Deactivated;
        account.DeactivatedAt = timeProvider.GetUtcNow();
        return await SaveAsync(account, "BankAccount.Deactivated", previous, null, ct => outbox.AddAsync(new BankAccountDeactivated(account.OrgId, account.Id), ct), cancellationToken);
    }

    /// <summary>The official email confirmed an added account (a <c>PayoutAccountChange</c> token); runs in the caller's transaction.</summary>
    public async Task<bool> ConfirmByEmailAsync(Guid organizationId, Guid accountId, CancellationToken cancellationToken)
    {
        if (await orgs.BankAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == accountId && a.OrgId == organizationId, cancellationToken) is not { Status: BankAccountStatus.ConfirmationPending } account)
        {
            return false;
        }

        account.Status = BankAccountStatus.Pending;
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("BankAccount.EmailConfirmed", "BankAccount", account.Id, account.OrgId,
            PreviousState: BankAccountStatus.ConfirmationPending, NewState: BankAccountStatus.Pending), cancellationToken);
        return true;
    }

    /// <summary><c>GET /admin/bank-accounts?status=</c>: staff see the full number to compare it with the NBS register.</summary>
    public async Task<IResult> QueueAsync(string? status, CancellationToken cancellationToken)
    {
        var rows = await (from a in orgs.BankAccounts.IgnoreQueryFilters().AsNoTracking()
                          join o in orgs.Organizations.IgnoreQueryFilters().AsNoTracking() on a.OrgId equals o.Id
                          where a.Status == (status ?? BankAccountStatus.Pending)
                          orderby a.CreatedAt
                          select new { a, o.Pib, o.LegalName })
            .Take(200)
            .ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(r => new { account = View(r.a, full: true, staff: true), pib = r.Pib, legalName = r.LegalName }));
    }

    /// <summary><c>POST /admin/bank-accounts/{id}/evidence</c>: the NBS register snapshot or a bank letter, stored as the organization's KYB evidence.</summary>
    public async Task<IResult> UploadEvidenceAsync(Guid id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (await orgs.BankAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        if (file is null || file.Length is 0 or > 10 * 1024 * 1024)
        {
            return Problems.Validation("file", "file_required", "Upload the snapshot (PDF or image, at most 10 MB).");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return await documentWriter.StoreAsync(account.OrgId, DocumentPurpose.KybEvidence, Path.GetFileName(file.FileName), content, cancellationToken) is { } stored
            ? Results.Created($"/api/v1/documents/{stored.Id}", new { documentId = stored.Id })
            : Problems.Error(StatusCodes.Status422UnprocessableEntity, "unsupported_file_type", "The snapshot must be a PDF or an image.");
    }

    /// <summary>
    /// <c>POST /admin/bank-accounts/{id}/verify</c> (step-up): the NBS snapshot is mandatory and the officer confirms that
    /// the holder is the organization (its PIB); the account is usable after the cool-down.
    /// </summary>
    public async Task<IResult> VerifyAsync(Guid id, BankAccountVerification body, CancellationToken cancellationToken)
    {
        if (await orgs.BankAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        if (account.Status != BankAccountStatus.Pending)
        {
            return Problems.ConflictState($"The account is {account.Status}.");
        }

        if (account.DuplicateFlag)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "duplicate_account", "The same account is registered by another organization; resolve the alert first.");
        }

        if (!body.HolderMatchesPib || body.EvidenceDocumentId is not { } evidenceId || !await OwnDocumentAsync(account.OrgId, evidenceId, cancellationToken)
            || (body.SupplementDocumentId is { } supplementId && !await OwnDocumentAsync(account.OrgId, supplementId, cancellationToken)))
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "evidence_missing",
                "Attach the NBS account register snapshot of this organization and confirm that the holder's PIB matches.");
        }

        var now = timeProvider.GetUtcNow();
        account.Status = BankAccountStatus.Verified;
        account.EvidenceDocumentId = evidenceId;
        account.SupplementDocumentId = body.SupplementDocumentId;
        account.VerifiedBy = UserId;
        account.VerifiedAt = now;
        account.UsableFrom = now.AddHours(await parameters.GetAsync(CoolDownHoursParameter, 72, cancellationToken));
        return await SaveAsync(account, "BankAccount.Verified", BankAccountStatus.Pending, null,
            ct => outbox.AddAsync(new BankAccountVerified(account.OrgId, account.Id, account.UsableFrom.Value), ct), cancellationToken);
    }

    /// <summary><c>POST /admin/bank-accounts/{id}/reject</c> (step-up) with a reason.</summary>
    public async Task<IResult> RejectAsync(Guid id, BankAccountReason body, CancellationToken cancellationToken)
    {
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000)
        {
            return Problems.Validation("reason", "reason_required", "A reason (at most 1000 characters) is required.");
        }

        if (await orgs.BankAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { } account)
        {
            return NotFound();
        }

        if (account.Status is not (BankAccountStatus.Pending or BankAccountStatus.ConfirmationPending))
        {
            return Problems.ConflictState($"The account is {account.Status}.");
        }

        var previous = account.Status;
        account.Status = BankAccountStatus.Rejected;
        account.RejectionReason = reason;
        return await SaveAsync(account, "BankAccount.Rejected", previous, reason, null, cancellationToken);
    }

    /// <summary><c>POST /admin/bank-accounts/{id}/clear-duplicate</c> (step-up): compliance resolved the duplicate alert for this record.</summary>
    public async Task<IResult> ClearDuplicateAsync(Guid id, BankAccountReason body, CancellationToken cancellationToken)
    {
        var reason = body.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 1000)
        {
            return Problems.Validation("reason", "reason_required", "A reason (at most 1000 characters) is required.");
        }

        if (await orgs.BankAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(a => a.Id == id, cancellationToken) is not { DuplicateFlag: true } account)
        {
            return NotFound();
        }

        account.DuplicateFlag = false;
        return await SaveAsync(account, "BankAccount.DuplicateCleared", account.Status, reason, null, cancellationToken);
    }

    /// <summary><c>GET /admin/bank-codes</c>.</summary>
    public async Task<IResult> BankCodesAsync(CancellationToken cancellationToken) =>
        Results.Ok(await orgs.BankCodes.AsNoTracking().OrderBy(c => c.Code).Select(c => new { code = c.Code, name = c.Name }).ToListAsync(cancellationToken));

    /// <summary><c>PUT /admin/bank-codes/{code}</c>: staff add or rename a bank; existing accounts keep the name they got.</summary>
    public async Task<IResult> SetBankCodeAsync(string code, BankCodeRequest body, CancellationToken cancellationToken)
    {
        var name = body.Name?.Trim();
        if (code.Length != 3 || !code.All(char.IsAsciiDigit) || string.IsNullOrEmpty(name) || name.Length > 200)
        {
            return Problems.Validation("code", "invalid_bank_code", "A 3-digit bank code and a name (at most 200 characters) are required.");
        }

        var existing = await orgs.BankCodes.SingleOrDefaultAsync(c => c.Code == code, cancellationToken);
        var previous = existing?.Name;
        if (existing is null)
        {
            orgs.BankCodes.Add(new BankCode { Code = code, Name = name });
        }
        else
        {
            existing.Name = name;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("BankCode.Set", "BankCode", Guid.Empty,
                    Changes: new Dictionary<string, string?> { ["code"] = code, ["name"] = $"{previous} → {name}" }), ct);
            },
            cancellationToken);
        return Results.Ok(new { code, name });
    }

    // ----- IBankAccountQuery -----

    public async Task<IReadOnlyList<FundingAccount>> UsableForFundingAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var accounts = await orgs.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.OrgId == organizationId && a.Status == BankAccountStatus.Verified && a.UsableFrom <= now)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        return accounts.Select(a => new FundingAccount(a.Id, a.OrgId,
            SerbianBankAccountNumber.TryParse(a.AccountNumber, out var number) ? number : throw new InvalidOperationException("A stored account number is invalid."),
            a.BankName, Currency.Rsd)).ToList();
    }

    public Task<bool> IsUsableForFundingAsync(Guid organizationId, Guid accountId, DateTimeOffset at, CancellationToken cancellationToken) =>
        orgs.BankAccounts.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(a => a.Id == accountId && a.OrgId == organizationId && a.Status == BankAccountStatus.Verified && a.UsableFrom <= at, cancellationToken);

    /// <summary>Evidence is a clean KYB-evidence document of the organization (staff upload it through the evidence endpoint).</summary>
    private async Task<bool> OwnDocumentAsync(Guid organizationId, Guid documentId, CancellationToken cancellationToken) =>
        await documents.GetAsync(documentId, cancellationToken) is { ScanStatus: ScanStatus.Clean, Purpose: DocumentPurpose.KybEvidence } document && document.OwnerOrgId == organizationId;

    private async Task<IResult> SaveAsync(BankAccount account, string action, string previous, string? reason, Func<CancellationToken, Task>? more, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry(action, "BankAccount", account.Id, account.OrgId, PreviousState: previous, NewState: account.Status, Reason: reason,
                        Changes: new Dictionary<string, string?> { ["accountNumber"] = Masked(account.AccountNumber) },
                        EvidenceDocumentIds: new[] { account.EvidenceDocumentId, account.SupplementDocumentId }.OfType<Guid>().ToList()), ct);
                    if (more is not null)
                    {
                        await more(ct);
                    }
                },
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.ConcurrencyConflict();
        }

        return Results.Ok(View(account, full: true, staff: request.ActorType == ActorType.User && account.OrgId != request.OrganizationId));
    }

    private static string Masked(string accountNumber) =>
        SerbianBankAccountNumber.TryParse(accountNumber, out var number) ? number.ToString() : "***";

    /// <summary>
    /// Owners and staff see the full number; anyone else only the bank code and last four account digits (task 009).
    /// The duplicate flag is for staff: it would tell the owner that the number is registered elsewhere.
    /// </summary>
    private static object View(BankAccount a, bool full, bool staff) => new
    {
        id = a.Id,
        accountNumber = full && SerbianBankAccountNumber.TryParse(a.AccountNumber, out var number) ? number.ToDashedString() : Masked(a.AccountNumber),
        currency = a.Currency,
        bankName = a.BankName,
        status = a.Status,
        duplicateFlag = staff ? a.DuplicateFlag : (bool?)null,
        verifiedAt = a.VerifiedAt,
        usableFrom = a.UsableFrom,
        deactivatedAt = a.DeactivatedAt,
        rejectionReason = a.RejectionReason,
        version = a.Version,
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}
