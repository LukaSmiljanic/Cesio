using Microsoft.EntityFrameworkCore;
using Modules.Notifications.Contracts;
using Modules.Notifications.Persistence;

namespace Modules.Notifications;

internal sealed class NotificationStats(NotificationsDbContext notifications) : INotificationStats
{
    public Task<int> FailedEmailsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        notifications.Notifications.CountAsync(n => n.EmailStatus == EmailStatus.Failed && n.CreatedAt >= since, cancellationToken);
}
