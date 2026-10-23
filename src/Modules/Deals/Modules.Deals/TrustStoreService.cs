using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Deals.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Deals;

public sealed record AddTrustedCertificateRequest(string? Name, string? Certificate);

/// <summary>
/// The trust store of the signature validator (task 016 requirement 2): Serbian QTSP root and intermediate
/// certificates, maintained by platform staff from the official register; additions and deactivations are audited.
/// </summary>
internal sealed class TrustStoreService(DealsDbContext deals, UnitOfWork unitOfWork, IAuditWriter audit, IRequestContext request, TimeProvider timeProvider)
{
    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    public async Task<IResult> ListAsync(CancellationToken cancellationToken) =>
        Results.Ok((await deals.TrustedCertificates.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken))
            .Select(c => new { id = c.Id, name = c.Name, sha256Thumbprint = c.Sha256Thumbprint, addedAt = c.AddedAt, active = c.DeactivatedAt is null }));

    public async Task<IResult> AddAsync(AddTrustedCertificateRequest body, CancellationToken cancellationToken)
    {
        byte[] der;
        try
        {
            der = Convert.FromBase64String(body.Certificate ?? string.Empty);
            using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give the certificate as Base64 DER.");
        }

        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 200)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", "Give a name of at most 200 characters.");
        }

        var thumbprint = Convert.ToHexStringLower(SHA256.HashData(der));
        if (await deals.TrustedCertificates.AnyAsync(c => c.Sha256Thumbprint == thumbprint, cancellationToken))
        {
            return Problems.Error(StatusCodes.Status409Conflict, "already_exists", "The certificate is already in the trust store.");
        }

        var entry = new TrustedCertificate
        {
            Id = SequentialGuid.NewGuid(),
            Name = body.Name.Trim(),
            Sha256Thumbprint = thumbprint,
            Certificate = Convert.ToBase64String(der),
            AddedBy = UserId,
            AddedAt = timeProvider.GetUtcNow(),
        };
        deals.TrustedCertificates.Add(entry);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await deals.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("TrustStore.CertificateAdded", "TrustedCertificate", entry.Id,
                    Changes: new Dictionary<string, string?> { ["name"] = entry.Name, ["sha256"] = thumbprint }), ct);
            },
            cancellationToken);
        return Results.Created($"/api/v1/admin/trusted-certificates/{entry.Id}", new { id = entry.Id, sha256Thumbprint = thumbprint });
    }

    public async Task<IResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await deals.TrustedCertificates.SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } entry)
        {
            return Problems.Error(StatusCodes.Status404NotFound, "not_found", "The certificate does not exist.");
        }

        if (entry.DeactivatedAt is not null)
        {
            return Problems.Error(StatusCodes.Status409Conflict, "conflict_state", "The certificate is already deactivated.");
        }

        entry.DeactivatedAt = timeProvider.GetUtcNow();
        entry.DeactivatedBy = UserId;
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await deals.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("TrustStore.CertificateDeactivated", "TrustedCertificate", id), ct);
            },
            cancellationToken);
        return Results.NoContent();
    }
}
