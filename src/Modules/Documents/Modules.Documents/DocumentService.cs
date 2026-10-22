using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Documents.Contracts;
using Modules.Documents.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Documents;

/// <summary>
/// Upload, scan and authorized download of documents (task 006, security.md §4). The owner organization is the
/// request's active organization; every member may upload and read its documents, since every member is an Owner
/// (ADR-020, security.md §3). Other readers need a module's <see cref="IDocumentAccessPolicy"/>.
/// </summary>
internal sealed class DocumentService(
    DocumentsDbContext documents,
    IDocumentStore store,
    IMalwareScanner scanner,
    IEnumerable<IDocumentAccessPolicy> policies,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IAccessLog accessLog,
    IRequestContext request,
    TimeProvider timeProvider)
{
    public const long MaxSizeBytes = 20 * 1024 * 1024;

    private const int MaxFileNameLength = 120;

    public async Task<IResult> UploadAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        if (!http.HasFormContentType)
        {
            return Problems.Validation("file", "A multipart form with a file and a purpose is required.");
        }

        var form = await http.ReadFormAsync(cancellationToken);
        if (form.Files.GetFile("file") is not { } file || file.Length == 0)
        {
            return Problems.Validation("file", "A file is required.");
        }

        if (!Enum.TryParse<DocumentPurpose>(form["purpose"].ToString(), ignoreCase: false, out var purpose) || !Enum.IsDefined(purpose))
        {
            return Problems.Validation("purpose", "An unknown document purpose.");
        }

        if (file.Length > MaxSizeBytes)
        {
            return Problems.Error(StatusCodes.Status413PayloadTooLarge, "file_too_large", "Files may be at most 20 MB.");
        }

        using var content = new MemoryStream();
        await file.CopyToAsync(content, cancellationToken);
        // The type comes from the file's first bytes, never from the client's Content-Type or file name.
        if (DetectContentType(content.GetBuffer().AsSpan(0, (int)content.Length)) is not { } contentType)
        {
            return Problems.Error(StatusCodes.Status415UnsupportedMediaType, "unsupported_file_type", "Only PDF, XML, PNG and JPEG files are accepted.");
        }

        var document = new StoredDocument
        {
            Id = SequentialGuid.NewGuid(),
            OwnerOrgId = request.OrganizationId ?? throw new InvalidOperationException("An organization context is required."),
            UploadedBy = request.UserId ?? throw new InvalidOperationException("An authenticated user is required."),
            Purpose = purpose,
            FileName = SanitizeFileName(file.FileName, contentType),
            ContentType = contentType,
            SizeBytes = content.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length))),
            BlobKey = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            ScanStatus = ScanStatus.Pending,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        content.Position = 0;
        await store.PutQuarantinedAsync(document.BlobKey, content, cancellationToken);
        document.ScanStatus = await ScanAndReleaseAsync(document.BlobKey, content, cancellationToken);
        document.ScannedAt = document.ScanStatus == ScanStatus.Pending ? null : timeProvider.GetUtcNow();
        documents.StoredDocuments.Add(document);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await documents.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("Document.Uploaded", "Document", document.Id, document.OwnerOrgId,
                    NewState: document.ScanStatus.ToString(),
                    Changes: new Dictionary<string, string?> { ["purpose"] = purpose.ToString(), ["sha256"] = document.Sha256, ["contentType"] = contentType }), ct);
            },
            cancellationToken);

        // An infected upload is kept in quarantine and never served; notifying the uploader is task 019.
        return Results.Created($"/api/v1/documents/{document.Id}", Metadata(document));
    }

    /// <summary>
    /// Scans the quarantined file and moves a clean one to the clean container. A scanner or storage failure leaves
    /// the file in quarantine as <see cref="ScanStatus.Failed"/>; it can never be downloaded (fail closed).
    /// </summary>
    private async Task<ScanStatus> ScanAndReleaseAsync(string blobKey, MemoryStream content, CancellationToken cancellationToken)
    {
        try
        {
            content.Position = 0;
            var verdict = await scanner.ScanAsync(content, cancellationToken);
            if (verdict == ScanStatus.Clean)
            {
                await store.ReleaseAsync(blobKey, cancellationToken);
            }

            return verdict;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ScanStatus.Failed;
        }
    }

    /// <summary>Owners see their documents in any scan state; readers through a module policy see only clean ones.</summary>
    public async Task<IResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await ReadableAsync(id, cancellationToken) is { } document
        && (document.OwnerOrgId == request.OrganizationId || document.ScanStatus == ScanStatus.Clean)
            ? Results.Ok(Metadata(document))
            : NotFound();

    public async Task<IResult> DownloadAsync(Guid id, HttpResponse response, CancellationToken cancellationToken)
    {
        if (await ReadableAsync(id, cancellationToken) is not { } document)
        {
            return NotFound();
        }

        if (document.ScanStatus != ScanStatus.Clean)
        {
            return document.OwnerOrgId == request.OrganizationId
                ? Problems.Error(StatusCodes.Status409Conflict, "document_not_available", "This document cannot be downloaded (scan not clean).")
                : NotFound();
        }

        // The blob is opened first, so the access log only records downloads that can actually happen.
        var stream = await store.OpenCleanAsync(document.BlobKey, cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(
            ct => accessLog.RecordAsync(new AccessLogEntry("Document.Downloaded", "Document", document.Id, document.OwnerOrgId), ct),
            cancellationToken);

        response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(stream, document.ContentType, fileDownloadName: document.FileName);
    }

    /// <summary>The document if the caller may read it: a member of the owner organization, or allowed by a policy.</summary>
    private async Task<StoredDocument?> ReadableAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.StoredDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null || request.UserId is not { } userId)
        {
            return null;
        }

        if (document.OwnerOrgId == request.OrganizationId)
        {
            return document;
        }

        var info = document.ToInfo();
        foreach (var policy in policies.Where(p => p.AppliesTo(document.Purpose)))
        {
            if (await policy.CanReadAsync(info, userId, request.OrganizationId, cancellationToken))
            {
                return document;
            }
        }

        return null;
    }

    /// <summary>PDF, XML, PNG or JPEG by magic bytes; null for anything else.</summary>
    internal static string? DetectContentType(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        var text = head.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? head[3..] : head;
        var start = 0;
        while (start < text.Length && start < 64 && text[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            start++;
        }

        // UBL is accepted with or without an XML declaration. Consumers must parse XML with DTDs prohibited (no XXE).
        var rest = text[start..];
        return rest.StartsWith("<?xml"u8) || (rest.Length > 1 && rest[0] == (byte)'<' && char.IsAsciiLetter((char)rest[1])) ? "application/xml" : null;
    }

    /// <summary>Keeps letters, digits, dot, dash and underscore; the extension follows the detected type.</summary>
    internal static string SanitizeFileName(string? name, string contentType)
    {
        // Windows paths are cut too, whatever the server OS.
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName((name ?? string.Empty).Replace('\\', '/')));
        var safe = new StringBuilder();
        foreach (var c in baseName)
        {
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        var extension = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            _ => ".xml",
        };
        var stem = safe.Length == 0 ? "document" : safe.ToString();
        return stem[..Math.Min(stem.Length, MaxFileNameLength - extension.Length)] + extension;
    }

    private static object Metadata(StoredDocument d) => new
    {
        id = d.Id,
        purpose = d.Purpose,
        fileName = d.FileName,
        contentType = d.ContentType,
        sizeBytes = d.SizeBytes,
        sha256 = d.Sha256,
        scanStatus = d.ScanStatus,
        createdAt = d.CreatedAt,
    };

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
}

internal sealed class DocumentQuery(DocumentsDbContext documents) : IDocumentQuery
{
    public async Task<DocumentInfo?> GetAsync(Guid documentId, CancellationToken cancellationToken) =>
        (await documents.StoredDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken))?.ToInfo();
}

internal static class Problems
{
    public static IResult Validation(string field, string message) =>
        Error(StatusCodes.Status400BadRequest, "validation_failed", message);

    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
