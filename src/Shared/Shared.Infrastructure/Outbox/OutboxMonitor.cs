using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Persistence.Platform;

namespace Shared.Infrastructure.Outbox;

public sealed record OutboxHealth(int Pending, int DeadLettered);

/// <summary>The outbox backlog for the admin dashboard (task 022).</summary>
public sealed class OutboxMonitor(PlatformDbContext platform)
{
    public async Task<OutboxHealth> HealthAsync(CancellationToken cancellationToken) => new(
        await platform.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null, cancellationToken),
        await platform.OutboxMessages.CountAsync(m => m.DeadLetteredAt != null, cancellationToken));
}
