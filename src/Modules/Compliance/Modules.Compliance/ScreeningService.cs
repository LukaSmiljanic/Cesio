using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Modules.Compliance.Contracts;
using Modules.Compliance.Persistence;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Compliance;

/// <summary>
/// Sanctions screening against locally imported lists (task 007, compliance.md §1). The lists are configuration (UN,
/// APML, EU, OFAC by default, LQ-31); in the MVP they are imported as CSV (one name per line), idempotently by content.
/// </summary>
internal sealed class ScreeningService(
    ComplianceDbContext compliance,
    IBusinessParameters parameters,
    UnitOfWork unitOfWork,
    IAuditWriter audit,
    IRequestContext request,
    TimeProvider timeProvider) : IScreeningService
{
    /// <summary>Jaro-Winkler threshold for a potential match (configurable, task 007).</summary>
    public const string Threshold = "screening.threshold";

    public static readonly string[] Sources = ["UN", "APML", "EU", "OFAC"];

    private const decimal DefaultThreshold = 0.92m;

    private const int MaxEntries = 100_000;

    public async Task<ScreeningAnswer> ScreenAsync(string name, CancellationToken cancellationToken)
    {
        var lists = await CurrentListsAsync(cancellationToken);
        if (lists.Count == 0)
        {
            return new ScreeningAnswer(ScreeningOutcome.NoLists, []);
        }

        var threshold = await parameters.GetAsync(Threshold, DefaultThreshold, cancellationToken);
        var normalized = NameMatcher.Normalize(name);
        var ids = lists.Select(l => l.Id).ToList();
        var entries = await compliance.ScreeningListEntries.AsNoTracking().Where(e => ids.Contains(e.ListId)).ToListAsync(cancellationToken);
        var hits = entries
            .Select(e => (Entry: e, Score: NameMatcher.Similarity(normalized, e.NormalizedName)))
            .Where(x => x.Score >= threshold)
            .OrderByDescending(x => x.Score)
            .Take(10)
            .Select(x => new ScreeningHit(lists.Single(l => l.Id == x.Entry.ListId).Source, x.Entry.Name, x.Score))
            .ToList();
        return new ScreeningAnswer(hits.Count == 0 ? ScreeningOutcome.Clear : ScreeningOutcome.PotentialMatch, hits);
    }

    /// <summary>Imports a list version from CSV; the same content again is a no-op (idempotent), every import is audited.</summary>
    public async Task<IResult> ImportAsync(string source, IFormFile? file, CancellationToken cancellationToken)
    {
        if (!Sources.Contains(source) || file is null || file.Length == 0)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", $"Upload a CSV file (one name per line) for {string.Join(", ", Sources)}.");
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var sha = Convert.ToHexStringLower(SHA256.HashData(content));
        if (await compliance.ScreeningLists.AnyAsync(l => l.Source == source && l.Sha256 == sha, cancellationToken))
        {
            return Results.Ok(new { source, sha256 = sha, imported = false });
        }

        var names = Encoding.UTF8.GetString(content).Split('\n')
            .Select(line => line.Trim().Trim('"').Trim())
            .Where(line => line.Length is > 1 and <= 400)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEntries + 1)
            .ToList();
        if (names.Count is 0 or > MaxEntries)
        {
            return Problems.Error(StatusCodes.Status400BadRequest, "validation_failed", $"The list must have between 1 and {MaxEntries} names.");
        }

        var list = new ScreeningList
        {
            Id = SequentialGuid.NewGuid(),
            Source = source,
            Sha256 = sha,
            EntryCount = names.Count,
            ImportedBy = request.UserId ?? Guid.Empty,
            ImportedAt = timeProvider.GetUtcNow(),
        };
        compliance.ScreeningLists.Add(list);
        compliance.ScreeningListEntries.AddRange(names.Select(n => new ScreeningListEntry { Id = SequentialGuid.NewGuid(), ListId = list.Id, Name = n, NormalizedName = NameMatcher.Normalize(n) }));
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                await compliance.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("ScreeningList.Imported", "ScreeningList", list.Id,
                    Changes: new Dictionary<string, string?> { ["source"] = source, ["sha256"] = sha, ["entries"] = names.Count.ToString(CultureInfo.InvariantCulture) }), ct);
            },
            cancellationToken);
        return Results.Ok(new { source, sha256 = sha, imported = true, entries = names.Count });
    }

    public async Task<IResult> ListsAsync(CancellationToken cancellationToken) =>
        Results.Ok((await CurrentListsAsync(cancellationToken)).Select(l => new { source = l.Source, sha256 = l.Sha256, entries = l.EntryCount, importedAt = l.ImportedAt }));

    /// <summary>The newest imported version of each source.</summary>
    private async Task<List<ScreeningList>> CurrentListsAsync(CancellationToken cancellationToken) =>
        (await compliance.ScreeningLists.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(l => l.Source).Select(g => g.OrderByDescending(l => l.ImportedAt).First()).ToList();
}

internal static class Problems
{
    public static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
