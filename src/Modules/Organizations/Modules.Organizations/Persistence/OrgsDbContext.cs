using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Organizations.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Modules.Organizations.Persistence;

/// <summary>
/// The <c>orgs</c> schema. Every org-owned table has a global query filter on the request's active organization
/// (security.md §3, defence in depth); cross-organization reads (my organizations, the membership check, invitation
/// acceptance) opt out explicitly with <c>IgnoreQueryFilters</c>.
/// </summary>
public sealed class OrgsDbContext : ModuleDbContext
{
    public const string SchemaName = "orgs";

    private readonly IRequestContext? _request;

    public OrgsDbContext(DbContextOptions<OrgsDbContext> options, UnitOfWork? unitOfWork, IRequestContext? request)
        : base(options, unitOfWork) => _request = request;

    /// <summary>For migrations and design-time tools, which have no request.</summary>
    public OrgsDbContext(DbContextOptions<OrgsDbContext> options, UnitOfWork? unitOfWork)
        : this(options, unitOfWork, null)
    {
    }

    internal DbSet<Organization> Organizations => Set<Organization>();

    internal DbSet<Membership> Memberships => Set<Membership>();

    internal DbSet<LegalRepresentative> LegalRepresentatives => Set<LegalRepresentative>();

    internal DbSet<TermsAcceptance> TermsAcceptances => Set<TermsAcceptance>();

    internal DbSet<Invitation> Invitations => Set<Invitation>();

    internal DbSet<SefConnection> SefConnections => Set<SefConnection>();

    internal DbSet<Pristupnica> Pristupnice => Set<Pristupnica>();

    internal DbSet<ConfirmationToken> ConfirmationTokens => Set<ConfirmationToken>();

    internal DbSet<MfaResetRequest> MfaResetRequests => Set<MfaResetRequest>();

    protected override string Schema => SchemaName;

    private Guid? CurrentOrganizationId => _request?.OrganizationId;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(org =>
        {
            org.ToTable("organizations", table => table.HasCheckConstraint("ck_organization_org_type", "[org_type] IN ('Seller','Factor')"));
            org.HasKey(o => o.Id);
            org.Property(o => o.Id).ValueGeneratedNever();
            org.Property(o => o.OrgType).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            org.Property(o => o.OnboardingStatus).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            org.Property(o => o.LegalName).HasMaxLength(200);
            org.Property(o => o.Pib).HasMaxLength(9).IsFixedLength().IsUnicode(false);
            org.Property(o => o.Mb).HasMaxLength(8).IsFixedLength().IsUnicode(false);
            org.Property(o => o.LegalForm).HasMaxLength(200);
            org.Property(o => o.RegisteredAddress).HasMaxLength(400);
            org.Property(o => o.OfficialEmail).HasMaxLength(200);
            org.Property(o => o.RiskRating).HasMaxLength(40);
            // PIB and MB are unique among claimed organizations only; Draft claims may share a PIB (ADR-020).
            org.HasIndex(o => o.Pib).IsUnique().HasFilter("[claimed_at] IS NOT NULL").HasDatabaseName("ux_org_pib_claimed");
            org.HasIndex(o => o.Mb).IsUnique().HasFilter("[claimed_at] IS NOT NULL AND [mb] IS NOT NULL").HasDatabaseName("ux_org_mb_claimed");
            org.HasQueryFilter(o => o.Id == CurrentOrganizationId);
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.HasKey(m => m.Id);
            membership.Property(m => m.Id).ValueGeneratedNever();
            membership.Property(m => m.Role).HasMaxLength(40).IsUnicode(false);
            membership.Property(m => m.Status).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            membership.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrgId);
            membership.HasOne<LegalRepresentative>().WithMany().HasForeignKey(m => m.LegalRepresentativeId).OnDelete(DeleteBehavior.Restrict);
            membership.HasIndex(m => new { m.OrgId, m.UserId }).IsUnique();
            membership.HasIndex(m => m.UserId);
            membership.HasQueryFilter(m => m.OrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<LegalRepresentative>(representative =>
        {
            representative.ToTable("legal_representatives");
            representative.HasKey(r => r.Id);
            representative.Property(r => r.Id).ValueGeneratedNever();
            representative.Property(r => r.Name).HasMaxLength(200);
            representative.Property(r => r.Role).HasMaxLength(200);
            representative.Property(r => r.Source).HasMaxLength(40).IsUnicode(false);
            representative.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrgId);
            representative.HasQueryFilter(r => r.OrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<TermsAcceptance>(terms =>
        {
            terms.ToTable("terms_acceptances");
            terms.HasKey(t => t.Id);
            terms.Property(t => t.Id).ValueGeneratedNever();
            terms.Property(t => t.TermsVersion).HasMaxLength(40);
            terms.Property(t => t.IpAddress).HasMaxLength(45).IsUnicode(false);
            terms.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrgId);
            terms.HasQueryFilter(t => t.OrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("invitations");
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.Email).HasMaxLength(256);
            invitation.Property(i => i.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrgId);
            invitation.HasQueryFilter(i => i.OrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<SefConnection>(connection =>
        {
            connection.ToTable("sef_connections");
            connection.HasKey(c => c.OrgId);
            connection.Property(c => c.SecretName).HasMaxLength(127).IsUnicode(false);
            connection.Property(c => c.Status).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            connection.Property(c => c.SupplierPib).HasMaxLength(9).IsFixedLength().IsUnicode(false);
            connection.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrgId);
            connection.HasQueryFilter(c => c.OrgId == CurrentOrganizationId);
        });

        modelBuilder.Entity<Pristupnica>(pristupnica =>
        {
            pristupnica.ToTable("pristupnice");
            pristupnica.HasKey(p => p.Id);
            pristupnica.Property(p => p.Id).ValueGeneratedNever();
            pristupnica.Property(p => p.GeneratedSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            pristupnica.Property(p => p.Method).HasMaxLength(30).IsUnicode(false);
            pristupnica.Property(p => p.Status).HasMaxLength(20).IsUnicode(false);
            pristupnica.Property(p => p.SignerName).HasMaxLength(200);
            pristupnica.Property(p => p.Notes).HasMaxLength(1000);
            pristupnica.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrgId).OnDelete(DeleteBehavior.Restrict);
            pristupnica.HasIndex(p => new { p.OrgId, p.CreatedAt });
        });

        modelBuilder.Entity<ConfirmationToken>(token =>
        {
            token.ToTable("confirmation_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.Id).ValueGeneratedNever();
            token.Property(t => t.Purpose).HasMaxLength(30).IsUnicode(false);
            token.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasOne<Organization>().WithMany().HasForeignKey(t => t.OrgId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MfaResetRequest>(reset =>
        {
            reset.ToTable("mfa_reset_requests", table => table.HasCheckConstraint("ck_mfa_reset_four_eyes", "[executed_by] IS NULL OR [first_officer] IS NULL OR [executed_by] <> [first_officer]"));
            reset.HasKey(r => r.Id);
            reset.Property(r => r.Id).ValueGeneratedNever();
            reset.Property(r => r.ApprovalKind).HasMaxLength(10).IsUnicode(false);
            reset.Property(r => r.Status).HasMaxLength(20).IsUnicode(false);
            reset.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrgId).OnDelete(DeleteBehavior.Restrict);
            reset.HasIndex(r => new { r.UserId, r.Status });
        });
    }
}

/// <summary>
/// The signed accession (pristupnica, ADR-020): generated from the organization's data, uploaded signed (QES, or a scan
/// confirmed through the official email), and accepted or rejected by compliance (task 007a).
/// </summary>
internal sealed class Pristupnica
{
    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    public Guid GeneratedDocumentId { get; init; }

    public required string GeneratedSha256 { get; init; }

    public Guid? SignedDocumentId { get; set; }

    /// <summary><c>Qes</c> or <c>ScannedConfirmedByEmail</c>.</summary>
    public string? Method { get; set; }

    /// <summary><c>Generated</c>, <c>Uploaded</c>, <c>EmailConfirmed</c>, <c>Accepted</c> or <c>Rejected</c>.</summary>
    public required string Status { get; set; }

    public string? SignerName { get; set; }

    public Guid? MatchedRepresentativeId { get; set; }

    public Guid? PowerOfAttorneyDocumentId { get; set; }

    public string? Notes { get; set; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public int Version { get; set; }
}

/// <summary>A single-use link sent to the official email (task 007a); only the SHA-256 of the token is stored.</summary>
internal sealed class ConfirmationToken
{
    public Guid Id { get; init; }

    public Guid OrgId { get; init; }

    /// <summary><c>Pristupnica</c>, <c>PayoutAccountChange</c>, <c>SefKeyChange</c> or <c>MfaReset</c>.</summary>
    public required string Purpose { get; init; }

    public Guid? SubjectId { get; init; }

    public required string TokenHash { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// An MFA reset without recovery codes (security.md §1): requested by the user, approved through the official email
/// (seller) or by another Owner (factor), then executed by compliance (two officers when only the email approved).
/// </summary>
internal sealed class MfaResetRequest
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public Guid OrgId { get; init; }

    /// <summary><c>Email</c> or <c>Owner</c>.</summary>
    public required string ApprovalKind { get; init; }

    /// <summary><c>Requested</c>, <c>Approved</c>, <c>FirstExecution</c> or <c>Executed</c>.</summary>
    public required string Status { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public Guid? FirstOfficer { get; set; }

    public Guid? ExecutedBy { get; set; }

    public DateTimeOffset? ExecutedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public int Version { get; set; }
}

internal sealed class OrgsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrgsDbContext>
{
    public OrgsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<OrgsDbContext>(OrgsDbContext.SchemaName);
}
