using Integrations.Sef;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Notifications.Contracts;
using Modules.Identity.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Secrets;

namespace Modules.Organizations;

public sealed record SefConnectionRequest(string ApiKey);

/// <summary>
/// <c>PUT /organizations/{id}/sef-connection</c> (task 010 item 3): the key is validated against SEF before it is
/// stored, and only if SEF returns invoices issued under the organization's PIB. The key is never returned or logged.
/// Confirming a key change through the organization's official APR email (security.md §2) comes with task 007a.
/// </summary>
internal sealed class SefConnectionService(
    OrgsDbContext orgs,
    ISefClient sef,
    ISecretStore secrets,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    IEmailSender email,
    IUserDirectory users,
    ConfirmationTokens confirmations,
    TimeProvider timeProvider) : ISefConnections
{
    /// <summary>How far back invoices are looked up to prove the PIB.</summary>
    private static readonly int ProofWindowDays = 90;

    private static readonly TimeSpan ClaimLockTimeout = TimeSpan.FromSeconds(30);

    private static string KeyFingerprint(string apiKey) => ConfirmationTokens.Hash(apiKey);

    public static string SecretName(Guid organizationId) => $"sef-key-{organizationId:N}";

    /// <summary>A validated new key waiting for the official-email confirmation (security.md §2).</summary>
    public static string PendingSecretName(Guid organizationId) => $"sef-key-pending-{organizationId:N}";

    public async Task<IResult> GetStatusAsync(CancellationToken cancellationToken) =>
        await orgs.SefConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken) is { } connection
            ? Results.Ok(new { status = connection.Status, validatedAt = connection.ValidatedAt, supplierPib = connection.SupplierPib })
            : Results.Ok(new { status = (string?)null, validatedAt = (DateTimeOffset?)null, supplierPib = (string?)null });

    public async Task<IResult> ConnectAsync(SefConnectionRequest body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.ApiKey) || body.ApiKey.Length > 200)
        {
            return Problems.Validation("apiKey", "required", "An API key from SEF is required.");
        }

        var organization = await orgs.Organizations.SingleAsync(cancellationToken);
        if (organization.OrgType != OrganizationType.Seller)
        {
            return Problems.ConflictState("Only seller organizations connect SEF.");
        }

        var apiKey = body.ApiKey.Trim();
        string? supplierPib;
        try
        {
            await sef.CheckKeyAsync(apiKey, cancellationToken);
            supplierPib = await SupplierPibAsync(apiKey, cancellationToken);
        }
        catch (SefUnauthorizedException)
        {
            return await RecordInvalidAsync(organization.Id, "sef_key_rejected", "SEF rejected the API key.", cancellationToken);
        }
        catch (SefUnavailableException)
        {
            return Problems.Error(StatusCodes.Status503ServiceUnavailable, "upstream_unavailable", "SEF is not available. Try again later.");
        }
        catch (UblFormatException)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "sef_pib_unverified", "The PIB could not be read from SEF invoices.");
        }

        if (supplierPib is null)
        {
            return Problems.Error(StatusCodes.Status422UnprocessableEntity, "sef_no_invoices",
                $"SEF has no sales invoices of the last {ProofWindowDays} days for this key, so the PIB cannot be proven.");
        }

        if (supplierPib != organization.Pib)
        {
            return await RecordInvalidAsync(organization.Id, "sef_pib_mismatch", "The SEF key belongs to a company with another PIB.", cancellationToken);
        }

        // A closed Draft holding a valid key for a claimed PIB is the two-users case of ADR-020; an expired one starts again.
        if (organization.OnboardingStatus == OnboardingStatus.ClaimClosed)
        {
            return await orgs.Organizations.IgnoreQueryFilters().AnyAsync(o => o.Pib == organization.Pib && o.ClaimedAt != null && o.Id != organization.Id, cancellationToken)
                ? await ClaimConflictAsync(organization, cancellationToken)
                : Problems.ConflictState("This registration expired. Start a new registration.");
        }

        // A claimed organization changes its key only through the official email in APR (ADR-020, security.md §2).
        if (organization.ClaimedAt is not null)
        {
            return await RequestKeyChangeAsync(organization, apiKey, cancellationToken);
        }

        // ADR-020, task 007a: only a valid SEF key claims a PIB. Claims of one PIB are serialised by an application
        // lock; a second organization with a valid key is refused and alerted (the unique index is the backstop).
        await secrets.SetAsync(SecretName(organization.Id), apiKey, cancellationToken);
        var existing = await orgs.SefConnections.SingleOrDefaultAsync(cancellationToken);
        var connection = Upsert(existing, organization.Id, SefConnectionStatus.Connected, supplierPib);
        organization.ClaimedAt = timeProvider.GetUtcNow();
        var closedDrafts = new List<Organization>();
        var claimedByAnother = false;
        try
        {
            await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await ApplicationLock.AcquireForTransactionAsync(unitOfWork.RequiredTransaction, $"orgs:claim:{organization.Pib}", ClaimLockTimeout, ct);
                    claimedByAnother = await orgs.Organizations.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(o => o.Pib == organization.Pib && o.ClaimedAt != null && o.Id != organization.Id, ct);
                    if (claimedByAnother)
                    {
                        return;
                    }

                    closedDrafts = await orgs.Organizations.IgnoreQueryFilters()
                        .Where(o => o.Pib == organization.Pib && o.ClaimedAt == null && o.Id != organization.Id && o.OnboardingStatus == OnboardingStatus.Draft)
                        .ToListAsync(ct);
                    foreach (var draft in closedDrafts)
                    {
                        draft.OnboardingStatus = OnboardingStatus.ClaimClosed;
                    }

                    await orgs.SaveChangesAsync(ct);
                    await audit.WriteAsync(new AuditEntry("Organization.ClaimWon", "Organization", organization.Id, organization.Id, Reason: "sef_key_valid"), ct);
                    foreach (var draft in closedDrafts)
                    {
                        await audit.WriteAsync(new AuditEntry("Organization.ClaimClosed", "Organization", draft.Id, draft.Id,
                            PreviousState: nameof(OnboardingStatus.Draft), NewState: nameof(OnboardingStatus.ClaimClosed), Reason: "pib_claimed_by_another_organization"), ct);
                    }

                    await audit.WriteAsync(new AuditEntry(existing is null ? "Organization.SefKeySet" : "Organization.SefKeyRotated", "Organization",
                        organization.Id, organization.Id, NewState: nameof(SefConnectionStatus.Connected)), ct);
                    await outbox.AddAsync(new SefConnectionValidated(organization.Id, supplierPib), ct);
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // This Draft was closed by the claim that won, or the unique index caught a claim outside the lock.
            claimedByAnother = true;
        }

        if (claimedByAnother)
        {
            orgs.ChangeTracker.Clear();
            await secrets.DeleteAsync(SecretName(organization.Id), cancellationToken);
            return await ClaimConflictAsync(organization, cancellationToken);
        }

        await NotifyClosedDraftsAsync(closedDrafts, cancellationToken);
        return Results.Ok(new { status = connection.Status, validatedAt = connection.ValidatedAt, supplierPib });
    }

    /// <summary>The confirmation link was used: the pending key replaces the stored one. Runs in the caller's transaction.</summary>
    /// <returns>False when no pending key exists (already applied, or replaced by a newer request).</returns>
    public async Task<bool> ApplyPendingKeyAsync(Guid organizationId, string? fingerprint, CancellationToken cancellationToken)
    {
        // The link applies the key it was sent for; a newer request replaced the pending key and voided this link.
        if (await secrets.GetAsync(PendingSecretName(organizationId), cancellationToken) is not { } apiKey || KeyFingerprint(apiKey) != fingerprint)
        {
            return false;
        }

        var connection = await orgs.SefConnections.IgnoreQueryFilters().SingleAsync(c => c.OrgId == organizationId, cancellationToken);
        var previous = connection.Status;
        await secrets.SetAsync(SecretName(organizationId), apiKey, cancellationToken);
        await secrets.DeleteAsync(PendingSecretName(organizationId), cancellationToken);
        var now = timeProvider.GetUtcNow();
        connection.Status = SefConnectionStatus.Connected;
        connection.ValidatedAt = now;
        connection.UpdatedAt = now;
        await orgs.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("Organization.SefKeyRotated", "Organization", organizationId, organizationId,
            PreviousState: previous.ToString(), NewState: nameof(SefConnectionStatus.Connected), Reason: "official_email_confirmed"), cancellationToken);
        await outbox.AddAsync(new SefConnectionValidated(organizationId, connection.SupplierPib ?? string.Empty), cancellationToken);
        await outbox.AddAsync(new OrganizationNotificationRequested(organizationId, NotificationTypes.Security, $"/organizations/{organizationId}", null), cancellationToken);
        return true;
    }

    public async Task<SefConnectionInfo?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await orgs.SefConnections.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.OrgId == organizationId, cancellationToken) is { } c
            ? new SefConnectionInfo(c.OrgId, c.Status, c.SecretName, c.SupplierPib)
            : null;

    public async Task MarkInvalidAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var connection = await orgs.SefConnections.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.OrgId == organizationId, cancellationToken);
        if (connection is null || connection.Status == SefConnectionStatus.Invalid)
        {
            return;
        }

        connection.Status = SefConnectionStatus.Invalid;
        connection.UpdatedAt = timeProvider.GetUtcNow();
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Organization.SefKeyInvalidated", "Organization", organizationId, organizationId,
                    PreviousState: nameof(SefConnectionStatus.Connected), NewState: nameof(SefConnectionStatus.Invalid)), ct);
                await outbox.AddAsync(new SefConnectionInvalidated(organizationId), ct);
            },
            cancellationToken);
    }

    /// <summary>The supplier PIB of the newest sales invoice in the proof window, or null when there is none.</summary>
    private async Task<string?> SupplierPibAsync(string apiKey, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var ids = await sef.ListSalesInvoiceIdsAsync(apiKey, today.AddDays(-ProofWindowDays), today, cancellationToken);
        if (ids.Count == 0)
        {
            return null;
        }

        return UblParser.Parse(await sef.GetSalesInvoiceUblAsync(apiKey, ids.Max(), cancellationToken)).Supplier.Pib;
    }

    /// <summary>
    /// Another organization already owns the PIB, and this one also holds a valid SEF key for it: refused, and a
    /// compliance alert (an audit event) is raised, so compliance resolves it through the APR legal representative.
    /// </summary>
    private async Task<IResult> ClaimConflictAsync(Organization organization, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteInTransactionAsync(
            ct => audit.WriteAsync(new AuditEntry("Compliance.Alert", "Organization", organization.Id, organization.Id, Reason: "two_valid_sef_keys_for_one_pib"), ct),
            cancellationToken);
        return Problems.Error(StatusCodes.Status409Conflict, "pib_already_claimed", "This company is already registered on Cesio. Ask its administrator for access.");
    }

    /// <summary>The users of closed Draft claims learn that the company is registered, without anything about its owner.</summary>
    private async Task NotifyClosedDraftsAsync(List<Organization> closedDrafts, CancellationToken cancellationToken)
    {
        foreach (var draft in closedDrafts)
        {
            foreach (var userId in await orgs.Memberships.IgnoreQueryFilters().Where(m => m.OrgId == draft.Id).Select(m => m.UserId).ToListAsync(cancellationToken))
            {
                if (await users.GetEmailAsync(userId, cancellationToken) is { } address)
                {
                    await email.SendAsync(new EmailMessage(address, "Cesio: firma je već registrovana",
                        $"Firma sa PIB-om {draft.Pib} je već registrovana na platformi Cesio. Za pristup se obratite njenom administratoru."), cancellationToken);
                }
            }
        }
    }

    /// <summary>A key change of a claimed organization: the validated key waits until the official email confirms it.</summary>
    private async Task<IResult> RequestKeyChangeAsync(Organization organization, string apiKey, CancellationToken cancellationToken)
    {
        if (organization.OfficialEmail is null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "official_email_missing",
                "The company's official email from APR is not recorded yet; compliance must record it before the SEF key can change.");
        }

        await secrets.SetAsync(PendingSecretName(organization.Id), apiKey, cancellationToken);
        EmailMessage? confirmation = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await audit.WriteAsync(new AuditEntry("Organization.SefKeyChangeRequested", "Organization", organization.Id, organization.Id), ct);
                confirmation = await confirmations.IssueAsync(organization, ConfirmationPurposes.SefKeyChange, organization.Id, "promena SEF API ključa", ct, KeyFingerprint(apiKey));
                await outbox.AddAsync(new OrganizationNotificationRequested(organization.Id, NotificationTypes.Security, $"/organizations/{organization.Id}", null), ct);
            },
            cancellationToken);
        await confirmations.SendAsync(confirmation!, cancellationToken);
        return Results.Accepted(value: new { status = "PendingConfirmation" });
    }

    /// <summary>A rejected key or another company's key: the connection is (or stays) Invalid, and the key is not stored.</summary>
    private async Task<IResult> RecordInvalidAsync(Guid organizationId, string errorCode, string detail, CancellationToken cancellationToken)
    {
        // A failed rotation keeps a working connection as it is: only a first or already broken connection becomes Invalid.
        var existing = await orgs.SefConnections.SingleOrDefaultAsync(cancellationToken);
        var keepsConnection = existing is { Status: SefConnectionStatus.Connected };
        if (!keepsConnection)
        {
            Upsert(existing, organizationId, SefConnectionStatus.Invalid, existing?.SupplierPib);
        }

        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Organization.SefKeyRejected", "Organization", organizationId, organizationId,
                    NewState: keepsConnection ? nameof(SefConnectionStatus.Connected) : nameof(SefConnectionStatus.Invalid), Reason: errorCode), ct);
            },
            cancellationToken);
        return Problems.Error(StatusCodes.Status422UnprocessableEntity, errorCode, detail);
    }

    private SefConnection Upsert(SefConnection? existing, Guid organizationId, SefConnectionStatus status, string? supplierPib)
    {
        var now = timeProvider.GetUtcNow();
        var connection = existing ?? new SefConnection { OrgId = organizationId, SecretName = SecretName(organizationId) };
        connection.Status = status;
        connection.SupplierPib = supplierPib;
        connection.UpdatedAt = now;
        if (status == SefConnectionStatus.Connected)
        {
            connection.ValidatedAt = now;
        }

        if (existing is null)
        {
            orgs.SefConnections.Add(connection);
        }

        return connection;
    }
}
