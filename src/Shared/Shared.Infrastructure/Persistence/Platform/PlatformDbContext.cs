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

    internal DbSet<JobRun> JobRuns => Set<JobRun>();

    internal DbSet<ParameterProposal> ParameterProposals => Set<ParameterProposal>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobRun>(run =>
        {
            run.ToTable("job_runs");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).ValueGeneratedNever();
            run.Property(r => r.Name).HasMaxLength(100).IsUnicode(false);
            run.Property(r => r.Status).HasMaxLength(20).IsUnicode(false);
            run.Property(r => r.Error).HasMaxLength(200);
            run.HasIndex(r => new { r.Name, r.StartedAt });
        });

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
            parameter.HasJsonCheck("value", scalar: true);
        });

        modelBuilder.Entity<ParameterProposal>(proposal =>
        {
            proposal.ToTable("parameter_proposals", table => table.HasCheckConstraint("ck_parameter_proposal_four_eyes", "[approved_by] IS NULL OR [approved_by] <> [proposed_by]"));
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ParamKey).HasMaxLength(200);
            proposal.Property(p => p.Status).HasMaxLength(20).IsUnicode(false);
            proposal.HasJsonCheck("value", scalar: true);
            proposal.HasIndex(p => p.Status);
        });
    }
}

internal sealed class PlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<PlatformDbContext>(PlatformDbContext.SchemaName);
}
