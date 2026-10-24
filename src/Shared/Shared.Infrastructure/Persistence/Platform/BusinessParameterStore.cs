using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Kernel;

namespace Shared.Infrastructure.Persistence.Platform;

public sealed record ParameterVersion(string Key, int Version, string Value, DateTimeOffset EffectiveFrom, Guid CreatedBy);

public sealed record ParameterProposalInfo(Guid Id, string Key, string Value, Guid ProposedBy, DateTimeOffset ProposedAt, string Status);

/// <summary>
/// Versioned business parameters for the admin editor (task 022, architecture.md §5): a change is a new version, never
/// an update. Money-related parameters are proposed and approved by a second person (four-eyes); the caller audits.
/// </summary>
public interface IBusinessParameterStore
{
    Task<IReadOnlyList<ParameterVersion>> CurrentAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ParameterVersion>> HistoryAsync(string key, CancellationToken cancellationToken);

    /// <summary>Adds the next version, effective now. In the caller's transaction.</summary>
    Task<ParameterVersion> AddVersionAsync(string key, string jsonValue, Guid createdBy, CancellationToken cancellationToken);

    Task<ParameterProposalInfo> ProposeAsync(string key, string jsonValue, Guid proposedBy, CancellationToken cancellationToken);

    Task<IReadOnlyList<ParameterProposalInfo>> PendingProposalsAsync(CancellationToken cancellationToken);

    /// <summary>Marks the proposal approved and adds its version. Null when it is not pending.</summary>
    Task<ParameterVersion?> ApproveAsync(Guid proposalId, Guid approvedBy, CancellationToken cancellationToken);
}

internal sealed class SqlBusinessParameterStore(PlatformDbContext platform, TimeProvider timeProvider) : IBusinessParameterStore
{
    public async Task<IReadOnlyList<ParameterVersion>> CurrentAsync(CancellationToken cancellationToken) =>
        (await platform.BusinessParameters.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(p => p.ParamKey)
            .Select(g => g.OrderByDescending(p => p.Version).First())
            .OrderBy(p => p.ParamKey, StringComparer.Ordinal)
            .Select(View)
            .ToList();

    public async Task<IReadOnlyList<ParameterVersion>> HistoryAsync(string key, CancellationToken cancellationToken) =>
        (await platform.BusinessParameters.AsNoTracking().Where(p => p.ParamKey == key).OrderByDescending(p => p.Version).ToListAsync(cancellationToken)).Select(View).ToList();

    public async Task<ParameterVersion> AddVersionAsync(string key, string jsonValue, Guid createdBy, CancellationToken cancellationToken)
    {
        using var _ = JsonDocument.Parse(jsonValue);
        var version = (await platform.BusinessParameters.Where(p => p.ParamKey == key).MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0) + 1;
        var parameter = new BusinessParameter { ParamKey = key, Version = version, Value = jsonValue, EffectiveFrom = timeProvider.GetUtcNow(), CreatedBy = createdBy };
        platform.BusinessParameters.Add(parameter);
        await platform.SaveChangesAsync(cancellationToken);
        return View(parameter);
    }

    public async Task<ParameterProposalInfo> ProposeAsync(string key, string jsonValue, Guid proposedBy, CancellationToken cancellationToken)
    {
        using var _ = JsonDocument.Parse(jsonValue);
        var proposal = new ParameterProposal { Id = SequentialGuid.NewGuid(), ParamKey = key, Value = jsonValue, ProposedBy = proposedBy, ProposedAt = timeProvider.GetUtcNow(), Status = "Pending" };
        platform.ParameterProposals.Add(proposal);
        await platform.SaveChangesAsync(cancellationToken);
        return View(proposal);
    }

    public async Task<IReadOnlyList<ParameterProposalInfo>> PendingProposalsAsync(CancellationToken cancellationToken) =>
        (await platform.ParameterProposals.AsNoTracking().Where(p => p.Status == "Pending").OrderBy(p => p.ProposedAt).ToListAsync(cancellationToken)).Select(View).ToList();

    public async Task<ParameterVersion?> ApproveAsync(Guid proposalId, Guid approvedBy, CancellationToken cancellationToken)
    {
        if (await platform.ParameterProposals.SingleOrDefaultAsync(p => p.Id == proposalId && p.Status == "Pending", cancellationToken) is not { } proposal)
        {
            return null;
        }

        proposal.Status = "Approved";
        proposal.ApprovedBy = approvedBy;
        proposal.ApprovedAt = timeProvider.GetUtcNow();
        await platform.SaveChangesAsync(cancellationToken);
        return await AddVersionAsync(proposal.ParamKey, proposal.Value, proposal.ProposedBy, cancellationToken);
    }

    private static ParameterVersion View(BusinessParameter p) => new(p.ParamKey, p.Version, p.Value, p.EffectiveFrom, p.CreatedBy);

    private static ParameterProposalInfo View(ParameterProposal p) => new(p.Id, p.ParamKey, p.Value, p.ProposedBy, p.ProposedAt, p.Status);
}

/// <summary>A proposed change of a money-related parameter waiting for a second person (task 022).</summary>
internal sealed class ParameterProposal
{
    public Guid Id { get; init; }

    public required string ParamKey { get; init; }

    public required string Value { get; init; }

    public Guid ProposedBy { get; init; }

    public DateTimeOffset ProposedAt { get; init; }

    /// <summary><c>Pending</c> or <c>Approved</c>.</summary>
    public required string Status { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public int Version { get; set; }
}
