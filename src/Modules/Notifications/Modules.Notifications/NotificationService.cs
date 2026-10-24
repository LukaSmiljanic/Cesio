using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.Identity.Contracts;
using Modules.Notifications.Contracts;
using Modules.Notifications.Persistence;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;
using Shared.Kernel;

namespace Modules.Notifications;

public sealed record PreferenceItem(string Type, string Channel, bool Enabled);

public sealed record PreferencesRequest(string? Language, IReadOnlyList<PreferenceItem>? Items);

internal static class EmailStatus
{
    public const string Pending = "Pending";

    public const string Digest = "Digest";

    public const string Sent = "Sent";

    public const string Failed = "Failed";

    public const string Skipped = "Skipped";
}

/// <summary>
/// Creates notifications for an organization's current members (task 019: recipients are resolved when the event is
/// handled, so removed members get nothing) and serves the in-app list and the preferences.
/// </summary>
internal sealed class NotificationService(
    NotificationsDbContext notifications,
    UnitOfWork unitOfWork,
    IOrganizationQuery organizations,
    IUserDirectory users,
    IRequestContext request,
    TimeProvider timeProvider)
{
    private static readonly string[] Channels = ["inapp", "email"];

    private Guid UserId => request.UserId ?? throw new InvalidOperationException("An authenticated user is required.");

    /// <summary>
    /// One notification per current member; for <see cref="NotificationTypes.Security"/> also an email-only one to the
    /// official email. Idempotent per event, recipient and type. Runs in the consumer's transaction.
    /// </summary>
    public async Task NotifyOrganizationAsync(Guid eventId, Guid orgId, string type, string link, string? reference, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var mandatory = NotificationTypes.Mandatory.Contains(type);
        var members = await organizations.ActiveMemberIdsAsync(orgId, cancellationToken);
        var optOuts = mandatory
            ? []
            : await notifications.Preferences.AsNoTracking().Where(p => members.Contains(p.UserId) && p.Type == type && !p.Enabled).ToListAsync(cancellationToken);
        var existing = await notifications.Notifications.AsNoTracking().Where(n => n.EventId == eventId && n.Type == type).Select(n => n.RecipientKey).ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            var key = member.ToString();
            if (existing.Contains(key) || optOuts.Any(o => o.UserId == member && o.Channel == "inapp"))
            {
                continue;
            }

            var email = optOuts.Any(o => o.UserId == member && o.Channel == "email") ? null : await users.GetEmailAsync(member, cancellationToken);
            notifications.Notifications.Add(New(eventId, key, member, orgId, type, link, reference, email, now));
        }

        if (type == NotificationTypes.Security && await organizations.OfficialEmailAsync(orgId, cancellationToken) is { } official)
        {
            var key = $"email:{official.ToLowerInvariant()}";
            if (!existing.Contains(key))
            {
                notifications.Notifications.Add(New(eventId, key, null, orgId, type, link, reference, official, now));
            }
        }

        try
        {
            await notifications.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A redelivery raced this one; the unique index keeps one notification per event, recipient and type.
            notifications.ChangeTracker.Clear();
        }
    }

    /// <summary><c>GET /notifications?before=&amp;limit=</c>: the user's in-app notifications, newest first (cursor = createdAt of the last item).</summary>
    public async Task<IResult> ListAsync(DateTimeOffset? before, int? limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 20, 1, 100);
        var rows = await notifications.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == UserId && (before == null || n.CreatedAt < before))
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new { id = n.Id, type = n.Type, link = n.Link, reference = n.Reference, organizationId = n.OrgId, createdAt = n.CreatedAt, readAt = n.ReadAt })
            .ToListAsync(cancellationToken);
        return Results.Ok(new { items = rows, next = rows.Count == take ? rows[^1].createdAt : (DateTimeOffset?)null });
    }

    public async Task<IResult> UnreadCountAsync(CancellationToken cancellationToken) =>
        Results.Ok(new { unread = await notifications.Notifications.CountAsync(n => n.RecipientUserId == UserId && n.ReadAt == null, cancellationToken) });

    /// <summary><c>POST /notifications/{id}/read</c>; another user's notification is 404.</summary>
    public async Task<IResult> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await notifications.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.RecipientUserId == UserId, cancellationToken) is not { } notification)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "not_found" });
        }

        notification.ReadAt ??= timeProvider.GetUtcNow();
        await unitOfWork.ExecuteInTransactionAsync(ct => notifications.SaveChangesAsync(ct), cancellationToken);
        return Results.NoContent();
    }

    /// <summary><c>GET /notification-preferences</c>: every type and channel, with whether it can be turned off.</summary>
    public async Task<IResult> PreferencesAsync(CancellationToken cancellationToken)
    {
        var saved = await notifications.Preferences.AsNoTracking().Where(p => p.UserId == UserId).ToListAsync(cancellationToken);
        var language = await notifications.UserSettings.AsNoTracking().Where(s => s.UserId == UserId).Select(s => s.Language).SingleOrDefaultAsync(cancellationToken) ?? "sr";
        return Results.Ok(new
        {
            language,
            items = NotificationTypes.All.SelectMany(type => Channels.Select(channel => new
            {
                type,
                channel,
                mandatory = NotificationTypes.Mandatory.Contains(type),
                enabled = NotificationTypes.Mandatory.Contains(type) || saved.SingleOrDefault(p => p.Type == type && p.Channel == channel)?.Enabled != false,
            })),
        });
    }

    /// <summary><c>PUT /notification-preferences</c>: optional types only; turning off a mandatory one is 422.</summary>
    public async Task<IResult> SetPreferencesAsync(PreferencesRequest body, CancellationToken cancellationToken)
    {
        if (body.Language is { } language && language is not ("sr" or "en"))
        {
            return Error(StatusCodes.Status400BadRequest, "validation_failed", "Language is sr or en.");
        }

        foreach (var item in body.Items ?? [])
        {
            if (!NotificationTypes.All.Contains(item.Type) || !Channels.Contains(item.Channel))
            {
                return Error(StatusCodes.Status400BadRequest, "validation_failed", $"Unknown type or channel: {item.Type}/{item.Channel}.");
            }

            if (NotificationTypes.Mandatory.Contains(item.Type) && !item.Enabled)
            {
                return Error(StatusCodes.Status422UnprocessableEntity, "notification_mandatory", $"{item.Type} notifications cannot be turned off.");
            }
        }

        foreach (var item in body.Items ?? [])
        {
            var preference = await notifications.Preferences.SingleOrDefaultAsync(p => p.UserId == UserId && p.Type == item.Type && p.Channel == item.Channel, cancellationToken);
            if (preference is null)
            {
                notifications.Preferences.Add(new NotificationPreference { UserId = UserId, Type = item.Type, Channel = item.Channel, Enabled = item.Enabled });
            }
            else
            {
                preference.Enabled = item.Enabled;
            }
        }

        if (body.Language is { } chosen)
        {
            var setting = await notifications.UserSettings.SingleOrDefaultAsync(s => s.UserId == UserId, cancellationToken);
            if (setting is null)
            {
                notifications.UserSettings.Add(new UserSetting { UserId = UserId, Language = chosen });
            }
            else
            {
                setting.Language = chosen;
            }
        }

        await unitOfWork.ExecuteInTransactionAsync(ct => notifications.SaveChangesAsync(ct), cancellationToken);
        return await PreferencesAsync(cancellationToken);
    }

    /// <summary>
    /// <c>GET /admin/notification-failures</c>: emails that failed after the retries (integration health, task 022).
    /// <c>lastError</c> is only an exception type (EmailDispatcher never stores a message, which may carry the address).
    /// </summary>
    public async Task<IResult> FailuresAsync(CancellationToken cancellationToken) =>
        Results.Ok(await notifications.Notifications.AsNoTracking()
            .Where(n => n.EmailStatus == EmailStatus.Failed)
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .Select(n => new { id = n.Id, type = n.Type, organizationId = n.OrgId, attempts = n.EmailAttempts, lastError = n.LastError, createdAt = n.CreatedAt })
            .ToListAsync(cancellationToken));

    private static Notification New(Guid eventId, string key, Guid? userId, Guid orgId, string type, string link, string? reference, string? email, DateTimeOffset now) => new()
    {
        Id = SequentialGuid.NewGuid(),
        EventId = eventId,
        RecipientKey = key,
        RecipientUserId = userId,
        OrgId = orgId,
        Type = type,
        Link = link,
        Reference = reference,
        Email = email,
        EmailStatus = email is null ? EmailStatus.Skipped : type == NotificationTypes.NewListing ? EmailStatus.Digest : EmailStatus.Pending,
        NextEmailAt = email is null ? null : now,
        CreatedAt = now,
    };

    private static IResult Error(int status, string errorCode, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
