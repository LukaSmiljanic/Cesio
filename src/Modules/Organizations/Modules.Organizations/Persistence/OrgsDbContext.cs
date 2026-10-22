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
    }
}

internal sealed class OrgsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrgsDbContext>
{
    public OrgsDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<OrgsDbContext>(OrgsDbContext.SchemaName);
}
