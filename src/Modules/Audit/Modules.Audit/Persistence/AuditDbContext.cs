using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Infrastructure.Persistence;

namespace Modules.Audit.Persistence;

/// <summary>The <c>audit</c> schema: the hash-chained <c>audit_events</c> and the <c>access_log</c> (audit.md §4).</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, UnitOfWork? unitOfWork)
    : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "audit";

    /// <summary>INSTEAD OF UPDATE, DELETE triggers that make both tables append-only (created in the migration).</summary>
    public const string AuditEventsTrigger = "tr_audit_events_append_only";

    public const string AccessLogTrigger = "tr_access_log_append_only";

    internal DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    internal DbSet<AccessLogRecord> AccessLog => Set<AccessLogRecord>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(audit =>
        {
            audit.ToTable("audit_events", table => table.HasTrigger(AuditEventsTrigger));
            audit.HasKey(e => e.Id);
            audit.Property(e => e.Id).ValueGeneratedNever();
            audit.HasIndex(e => e.Sequence).IsUnique();
            audit.Property(e => e.ActorType).HasMaxLength(200);
            audit.Property(e => e.Action).HasMaxLength(200);
            audit.Property(e => e.EntityType).HasMaxLength(200);
            audit.Property(e => e.PreviousState).HasMaxLength(200);
            audit.Property(e => e.NewState).HasMaxLength(200);
            audit.Property(e => e.CorrelationId).HasMaxLength(200);
            audit.Property(e => e.IdempotencyKey).HasMaxLength(200);
            audit.Property(e => e.IpAddress).HasMaxLength(45).IsUnicode(false);
            audit.Property(e => e.DeviceId).HasMaxLength(200);
            audit.Property(e => e.PrevHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            audit.Property(e => e.Hash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            audit.HasJsonCheck("related", nullable: true);
            audit.HasJsonCheck("changes", nullable: true);
            audit.HasJsonCheck("evidence_document_ids", nullable: true);
            audit.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt }).HasDatabaseName("ix_audit_entity");
            audit.HasIndex(e => new { e.OnBehalfOfOrgId, e.OccurredAt }).HasDatabaseName("ix_audit_org");
        });

        modelBuilder.Entity<AccessLogRecord>(access =>
        {
            access.ToTable("access_log", table => table.HasTrigger(AccessLogTrigger));
            access.HasKey(e => e.Id);
            access.Property(e => e.Id).ValueGeneratedNever();
            access.Property(e => e.ActorType).HasMaxLength(200);
            access.Property(e => e.Action).HasMaxLength(200);
            access.Property(e => e.EntityType).HasMaxLength(200);
            access.Property(e => e.CorrelationId).HasMaxLength(200);
            access.Property(e => e.IpAddress).HasMaxLength(45).IsUnicode(false);
            access.HasIndex(e => new { e.EntityType, e.EntityId, e.OccurredAt }).HasDatabaseName("ix_access_entity");
        });
    }
}

internal sealed class AuditDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<AuditDbContext>(AuditDbContext.SchemaName);
}
