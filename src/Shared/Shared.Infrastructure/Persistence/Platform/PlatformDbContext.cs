using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shared.Infrastructure.Persistence.Platform;

/// <summary>The <c>platform</c> schema: outbox, processed events, idempotency keys and business parameters (database.md §2).</summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, UnitOfWork? unitOfWork)
    : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "platform";

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    internal DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    internal DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    internal DbSet<BusinessParameter> BusinessParameters => Set<BusinessParameter>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(200);
            outbox.HasJsonCheck("payload");
            outbox.HasIndex(m => m.NextAttemptAt)
                .HasDatabaseName("ix_outbox_pending")
                .HasFilter("[processed_at] IS NULL AND [dead_lettered_at] IS NULL");
        });

        modelBuilder.Entity<ProcessedEvent>(processed =>
        {
            processed.ToTable("processed_events");
            processed.HasKey(p => new { p.EventId, p.Consumer });
            processed.Property(p => p.Consumer).HasMaxLength(200);
        });

        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("idempotency_keys");
            record.HasKey(r => new { r.OrgId, r.Endpoint, r.IdemKey });
            record.Property(r => r.IdemKey).HasMaxLength(IdempotencyRecord.MaxKeyLength);
            record.Property(r => r.Endpoint).HasMaxLength(200);
            record.Property(r => r.RequestSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            record.HasJsonCheck("response_body", nullable: true);
            record.HasIndex(r => r.ExpiresAt);
        });

        modelBuilder.Entity<BusinessParameter>(parameter =>
        {
            parameter.ToTable("business_parameters");
            parameter.HasKey(p => new { p.ParamKey, p.Version });
            parameter.Property(p => p.ParamKey).HasMaxLength(200);
            parameter.HasJsonCheck("value");
        });
    }
}

internal sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<PlatformDbContext>(PlatformDbContext.SchemaName);
}
