using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Infrastructure.Persistence;

namespace Modules.Notifications.Persistence;

/// <summary>The <c>notifications</c> schema (task 019): notifications with their email delivery, and per-user preferences.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "notifications";

    internal DbSet<Notification> Notifications => Set<Notification>();

    internal DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    internal DbSet<UserSetting> UserSettings => Set<UserSetting>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.RecipientKey).HasMaxLength(300);
            notification.Property(n => n.Email).HasMaxLength(256);
            notification.Property(n => n.Type).HasMaxLength(40).IsUnicode(false);
            notification.Property(n => n.Link).HasMaxLength(400);
            notification.Property(n => n.Reference).HasMaxLength(100);
            notification.Property(n => n.EmailStatus).HasMaxLength(20).IsUnicode(false);
            notification.Property(n => n.LastError).HasMaxLength(400);
            // Idempotent consumers (task 019 technical requirement): one notification per event, recipient and type.
            notification.HasIndex(n => new { n.EventId, n.RecipientKey, n.Type }).IsUnique();
            notification.HasIndex(n => new { n.RecipientUserId, n.CreatedAt });
            notification.HasIndex(n => new { n.EmailStatus, n.NextEmailAt });
        });

        modelBuilder.Entity<NotificationPreference>(preference =>
        {
            preference.ToTable("preferences");
            preference.HasKey(p => new { p.UserId, p.Type, p.Channel });
            preference.Property(p => p.Type).HasMaxLength(40).IsUnicode(false);
            preference.Property(p => p.Channel).HasMaxLength(10).IsUnicode(false);
        });

        modelBuilder.Entity<UserSetting>(setting =>
        {
            setting.ToTable("user_settings");
            setting.HasKey(s => s.UserId);
            setting.Property(s => s.Language).HasMaxLength(2).IsFixedLength().IsUnicode(false);
        });
    }
}

/// <summary>One notification for one recipient: a member (in-app and email) or, for security events, the official email only.</summary>
internal sealed class Notification
{
    public Guid Id { get; init; }

    /// <summary>The outbox event it came from (with the recipient and type, the idempotency key).</summary>
    public Guid EventId { get; init; }

    /// <summary>The user id, or <c>email:</c> plus the address for the official email.</summary>
    public required string RecipientKey { get; init; }

    public Guid? RecipientUserId { get; init; }

    public Guid OrgId { get; init; }

    public required string Type { get; init; }

    public required string Link { get; init; }

    public string? Reference { get; init; }

    /// <summary>The address resolved at send time; null when email is off.</summary>
    public string? Email { get; init; }

    /// <summary><c>Pending</c>, <c>Digest</c> (hourly batch), <c>Sent</c>, <c>Failed</c> or <c>Skipped</c>.</summary>
    public required string EmailStatus { get; set; }

    public int EmailAttempts { get; set; }

    public DateTimeOffset? NextEmailAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; set; }

    public int Version { get; set; }
}

/// <summary>An opt-out of an optional type on one channel (<c>email</c> or <c>inapp</c>).</summary>
internal sealed class NotificationPreference
{
    public Guid UserId { get; init; }

    public required string Type { get; init; }

    public required string Channel { get; init; }

    public bool Enabled { get; set; }
}

internal sealed class UserSetting
{
    public Guid UserId { get; init; }

    /// <summary><c>sr</c> (Serbian Latin, default) or <c>en</c>.</summary>
    public required string Language { get; set; }

    public int Version { get; set; }
}

internal sealed class NotificationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<NotificationsDbContext>(NotificationsDbContext.SchemaName);
}
