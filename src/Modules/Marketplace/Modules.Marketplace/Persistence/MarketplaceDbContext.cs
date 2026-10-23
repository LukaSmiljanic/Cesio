using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modules.Marketplace.Contracts;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Modules.Marketplace.Persistence;

/// <summary>
/// The <c>marketplace</c> schema (database.md §2): listings and their audience. Readers are sellers (their own
/// listings) and factors (through <see cref="ListingVisibility"/>), so there is no organization query filter; every
/// query states its own.
/// </summary>
public sealed class MarketplaceDbContext(DbContextOptions<MarketplaceDbContext> options, UnitOfWork? unitOfWork) : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "marketplace";

    internal DbSet<Listing> Listings => Set<Listing>();

    internal DbSet<ListingAudience> ListingAudience => Set<ListingAudience>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Listing>(listing =>
        {
            listing.ToTable("listings", table =>
            {
                table.HasCheckConstraint("ck_listing_dates", "[closes_at] > [opens_at]");
                table.HasCheckConstraint("ck_listing_reserve_price", "[reserve_price] IS NULL OR ([reserve_price] > 0 AND [reserve_price] < [face_value])");
            });
            listing.HasKey(l => l.Id);
            listing.Property(l => l.Id).ValueGeneratedNever();
            listing.Property(l => l.State).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            listing.Property(l => l.AudienceType).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
            listing.HasMoney(l => l.FaceValue, "face_value", "currency");
            listing.Property(l => l.ReservePriceAmount).HasColumnName("reserve_price");
            listing.Property(l => l.SellerPib).HasMaxLength(9).IsFixedLength().IsUnicode(false);
            listing.Property(l => l.DebtorPib).HasMaxLength(9).IsFixedLength().IsUnicode(false);
            listing.Property(l => l.SuspendedReason).HasMaxLength(400);
            // INV-R2: one Open listing per receivable (database.md §2).
            listing.HasIndex(l => l.ReceivableId).IsUnique()
                .HasFilter($"[state] = '{nameof(ListingState.Open)}'")
                .HasDatabaseName("ux_listing_open_per_receivable");
            listing.HasIndex(l => new { l.State, l.ClosesAt });
            listing.HasIndex(l => new { l.SellerOrgId, l.State });
        });

        modelBuilder.Entity<ListingAudience>(audience =>
        {
            audience.ToTable("listing_audience", table => table.HasCheckConstraint("ck_listing_audience_mode", "[mode] IN ('Include','Exclude')"));
            audience.HasKey(a => new { a.ListingId, a.FactorOrgId });
            audience.Property(a => a.Mode).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
            audience.HasOne<Listing>().WithMany().HasForeignKey(a => a.ListingId).OnDelete(DeleteBehavior.Restrict);
            audience.HasIndex(a => new { a.FactorOrgId, a.Mode });
        });
    }
}

/// <summary>
/// A time-boxed exposure of one receivable (domain-model.md). Face value, maturity and the seller's and debtor's PIB
/// are snapshots taken at opening (the invoice is immutable) so the feed can filter and apply the related-party rule
/// without reading another module's schema.
/// </summary>
internal sealed class Listing
{
    public Guid Id { get; init; }

    public Guid ReceivableId { get; init; }

    public Guid SellerOrgId { get; init; }

    public ListingState State { get; set; }

    public DateTimeOffset OpensAt { get; init; }

    public DateTimeOffset ClosesAt { get; init; }

    public AudienceType AudienceType { get; init; }

    public required Money FaceValue { get; init; }

    /// <summary>Hidden from factors; in the face value's currency.</summary>
    public decimal? ReservePriceAmount { get; init; }

    public DateOnly MaturityDate { get; init; }

    public required string SellerPib { get; init; }

    public required string DebtorPib { get; init; }

    public string? SuspendedReason { get; set; }

    public int Version { get; set; }
}

internal enum AudienceMode
{
    Include,
    Exclude,
}

internal sealed class ListingAudience
{
    public Guid ListingId { get; init; }

    public Guid FactorOrgId { get; init; }

    public AudienceMode Mode { get; init; }
}

internal sealed class MarketplaceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<MarketplaceDbContext>(MarketplaceDbContext.SchemaName);
}
