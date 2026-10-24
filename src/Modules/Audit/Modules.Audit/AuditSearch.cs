using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Audit.Contracts;
using Modules.Audit.Persistence;
using Modules.Identity.Contracts;
using Shared.Infrastructure;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Audit;

/// <param name="Before">Cursor: events with a lower sequence (newest first).</param>
public sealed record AuditFilter(string? EntityType, Guid? EntityId, Guid? OrganizationId, Guid? ActorUserId, string? Action, DateTimeOffset? From, DateTimeOffset? To, long? Before);

/// <summary>
/// Audit search and export (task 022) for Compliance Officers and Auditors. Every search and export is itself
/// access-logged; IP and user agent are not shown (LQ-38).
/// </summary>
internal sealed class AuditSearch(AuditDbContext audit, UnitOfWork unitOfWork, IAccessLog accessLog)
{
    private const int PageSize = 100;

    private const int MaxExportRows = 10_000;

    public async Task<IResult> SearchAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Query(filter).Take(PageSize).ToListAsync(cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(ct => accessLog.RecordAsync(new AccessLogEntry("Audit.Searched", "AuditLog", filter.EntityId ?? Guid.Empty, filter.OrganizationId), ct), cancellationToken);
        return Results.Ok(new { items = rows.Select(View), next = rows.Count == PageSize ? rows[^1].Sequence : (long?)null });
    }

    public async Task<IResult> ExportAsync(AuditFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Query(filter).Take(MaxExportRows).ToListAsync(cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(ct => accessLog.RecordAsync(new AccessLogEntry("Audit.Exported", "AuditLog", filter.EntityId ?? Guid.Empty, filter.OrganizationId), ct), cancellationToken);
        var csv = new StringBuilder("sequence;occurredAt;actorType;actorUserId;actorOrgId;onBehalfOfOrgId;action;entityType;entityId;previousState;newState;reason\r\n");
        foreach (var e in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{e.Sequence};{e.OccurredAt:O};{e.ActorType};{e.ActorUserId};{e.ActorOrgId};{e.OnBehalfOfOrgId};{CsvCell.Text(e.Action)};{CsvCell.Text(e.EntityType)};{e.EntityId};{CsvCell.Text(e.PreviousState)};{CsvCell.Text(e.NewState)};{CsvCell.Text(e.Reason)}\r\n");
        }

        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "audit.csv");
    }

    private IQueryable<AuditEvent> Query(AuditFilter f) =>
        audit.AuditEvents.AsNoTracking()
            .Where(e => (f.EntityType == null || e.EntityType == f.EntityType)
                && (f.EntityId == null || e.EntityId == f.EntityId)
                && (f.OrganizationId == null || e.OnBehalfOfOrgId == f.OrganizationId || e.ActorOrgId == f.OrganizationId)
                && (f.ActorUserId == null || e.ActorUserId == f.ActorUserId)
                && (f.Action == null || e.Action.StartsWith(f.Action))
                && (f.From == null || e.OccurredAt >= f.From)
                && (f.To == null || e.OccurredAt < f.To)
                && (f.Before == null || e.Sequence < f.Before))
            .OrderByDescending(e => e.Sequence);

    private static object View(AuditEvent e) => new
    {
        sequence = e.Sequence,
        occurredAt = e.OccurredAt,
        actorType = e.ActorType,
        actorUserId = e.ActorUserId,
        actorOrgId = e.ActorOrgId,
        onBehalfOfOrgId = e.OnBehalfOfOrgId,
        action = e.Action,
        entityType = e.EntityType,
        entityId = e.EntityId,
        previousState = e.PreviousState,
        newState = e.NewState,
        changes = e.Changes,
        reason = e.Reason,
        related = e.Related,
        correlationId = e.CorrelationId,
    };
}

public static class AuditEndpoints
{
    /// <summary><c>GET /admin/audit</c> and <c>GET /admin/audit/export</c> (task 022).</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/audit").RequireAuthorization().AddEndpointFilter(RequireAuditReader);
        admin.MapGet("", ([AsParameters] AuditFilter filter, AuditSearch search, CancellationToken ct) => search.SearchAsync(filter, ct));
        admin.MapGet("/export", ([AsParameters] AuditFilter filter, AuditSearch search, CancellationToken ct) => search.ExportAsync(filter, ct)).RequireStepUp();
        return app;
    }

    /// <summary>Compliance Officers and Auditors; anyone else gets 404 (security.md §3).</summary>
    private static async ValueTask<object?> RequireAuditReader(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var roles = http.RequestServices.GetRequiredService<IPlatformRoles>();
        return Guid.TryParse(http.User.FindFirst(CesioClaims.UserId)?.Value, out var userId)
            && (await roles.HasRoleAsync(userId, PlatformRoles.ComplianceOfficer, http.RequestAborted) || await roles.HasRoleAsync(userId, PlatformRoles.Auditor, http.RequestAborted))
                ? await next(context)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
    }
}
