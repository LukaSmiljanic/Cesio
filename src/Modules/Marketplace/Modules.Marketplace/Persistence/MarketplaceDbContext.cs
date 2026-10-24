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

    public const string OfferImmutableTrigger = "tr_offers_immutable";

    internal DbSet<Listing> Listings => Set<Listing>();

    internal DbSet<ListingAudience> ListingAudience => Set<ListingAudience>();

    internal DbSet<Offer> Offers => Set<Offer>();

    internal DbSet<FactorAppetite> FactorAppetites => Set<FactorAppetite>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FactorAppetite>(appetite =>
        {
            appetite.ToTable("factor_appetite", table =>
            {
                table.HasCheckConstraint("ck_factor_appetite_currency", "[currency] IN ('RSD','EUR')");
                table.HasCheckConstraint("ck_factor_appetite_range",
                    "([min_amount] IS NULL OR [min_amount] >= 0) AND ([max_amount] IS NULL OR [max_amount] >= 0) AND ([min_amount] IS NULL OR [max_amount] IS NULL OR [min_amount] <= [max_amount])");
            });
            appetite.HasKey(a => a.OrgId);
            appetite.Property(a => a.MinAmount).HasPrecision(19, 4);
            appetite.Property(a => a.MaxAmount).HasPrecision(19, 4);
            appetite.Property(a => a.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        });

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

        modelBuilder.Entity<Offer>(offer =>
        {
            offer.ToTable("offers", table =>
            {
                table.HasTrigger(OfferImmutableTrigger);
                table.HasCheckConstraint("ck_offer_purchase_price", "[purchase_price] > 0");
                table.HasCheckConstraint("ck_offer_funding_deadline", "[funding_deadline_bdays] BETWEEN 1 AND 30");
                table.HasCheckConstraint("ck_offer_fees", "[platform_fee] >= 0 AND [seller_fee] >= 0 AND [seller_fee] <= [platform_fee]");
            });
            offer.HasKey(o => o.Id);
            offer.Property(o => o.Id).ValueGeneratedNever();
            offer.HasMoney(o => o.PurchasePrice, "purchase_price", "currency");
            offer.Property(o => o.PlatformFeeAmount).HasColumnName("platform_fee");
            offer.Property(o => o.SellerFeeAmount).HasColumnName("seller_fee");
            offer.Property(o => o.FundingDeadlineBusinessDays).HasColumnName("funding_deadline_bdays");
            offer.Property(o => o.RecourseType).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            offer.Property(o => o.State).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            offer.Property(o => o.ConditionsText).HasMaxLength(1000);
            offer.Property(o => o.RejectionReason).HasMaxLength(400);
            offer.HasOne<Listing>().WithMany().HasForeignKey(o => o.ListingId).OnDelete(DeleteBehavior.Restrict);
            // One Active offer per factor per listing, one Accepted offer per listing (database.md §2).
            offer.HasIndex(o => new { o.ListingId, o.FactorOrgId }).IsUnique()
                .HasFilter($"[state] = '{nameof(OfferState.Active)}'")
                .HasDatabaseName("ux_offer_active_per_factor");
            offer.HasIndex(o => o.ListingId).IsUnique()
                .HasFilter($"[state] = '{nameof(OfferState.Accepted)}'")
                .HasDatabaseName("ux_offer_accepted_per_listing");
            offer.HasIndex(o => new { o.FactorOrgId, o.State });
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

/// <summary>
/// A binding, immutable offer (ADR-013): only the state (and its rejection reason) ever changes; the price, terms and
/// the fee snapshot are fixed at submission (financial-model.md §4) and guarded by a trigger and a column DENY.
/// </summary>
internal sealed class Offer
{
    public Guid Id { get; init; }

    public Guid ListingId { get; init; }

    public Guid FactorOrgId { get; init; }

    public required Money PurchasePrice { get; init; }

    public RecourseType RecourseType { get; init; }

    public int FundingDeadlineBusinessDays { get; init; }

    public DateTimeOffset ValidUntil { get; init; }

    public string? ConditionsText { get; init; }

    public int FeeScheduleVersion { get; init; }

    /// <summary>The whole platform fee for this price, in the price's currency.</summary>
    public decimal PlatformFeeAmount { get; init; }

    /// <summary>The seller's part of the fee (0 when the factor pays it all).</summary>
    public decimal SellerFeeAmount { get; init; }

    public bool VatPending { get; init; }

    public OfferState State { get; set; }

    public string? RejectionReason { get; set; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public int Version { get; set; }
}

internal sealed class MarketplaceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<MarketplaceDbContext>(MarketplaceDbContext.SchemaName);
}

/// <summary>A factor's appetite filters for the new-listing digest (task 019); amounts in <see cref="Currency"/> (RSD).</summary>
internal sealed class FactorAppetite
{
    public Guid OrgId { get; init; }

    public decimal? MinAmount { get; set; }

    public decimal? MaxAmount { get; set; }

    public string Currency { get; init; } = Shared.Kernel.Currency.Rsd.Code;

    public int? MinTenorDays { get; set; }

    public int? MaxTenorDays { get; set; }

    public bool DigestEnabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}
