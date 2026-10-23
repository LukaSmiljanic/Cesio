using Integrations.Sef;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Organizations.Contracts;
using Modules.Organizations.Persistence;
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
    TimeProvider timeProvider) : ISefConnections
{
    /// <summary>How far back invoices are looked up to prove the PIB.</summary>
    private static readonly int ProofWindowDays = 90;

    public static string SecretName(Guid organizationId) => $"sef-key-{organizationId:N}";

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

        await secrets.SetAsync(SecretName(organization.Id), apiKey, cancellationToken);
        var existing = await orgs.SefConnections.SingleOrDefaultAsync(cancellationToken);
        var connection = Upsert(existing, organization.Id, SefConnectionStatus.Connected, supplierPib);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await orgs.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry(existing is null ? "Organization.SefKeySet" : "Organization.SefKeyRotated", "Organization",
                    organization.Id, organization.Id, NewState: nameof(SefConnectionStatus.Connected)), ct);
                await outbox.AddAsync(new SefConnectionValidated(organization.Id, supplierPib), ct);
            },
            cancellationToken);
        return Results.Ok(new { status = connection.Status, validatedAt = connection.ValidatedAt, supplierPib });
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
