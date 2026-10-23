using Shared.Kernel;

namespace Modules.Fees.Contracts;

/// <summary>Who pays the platform fee (financial-model.md §4).</summary>
public enum FeePayer
{
    Factor,
    Seller,

    /// <summary>Shared: <see cref="FeeScheduleDefinition.SellerShareOfFee"/> of the fee is the seller's, the rest the factor's.</summary>
    Split,
}

/// <summary>What the fee is a share of; the MVP has only the discount <c>D = F − P</c> (ADR-007).</summary>
public enum FeeBasis
{
    Discount,
}

/// <summary>VAT on the fee; <c>TBD</c> until LQ-43 (VAT 0 with <see cref="FeeQuote.VatPending"/>).</summary>
public enum VatTreatment
{
    Standard20,
    Exempt,
    TBD,
}

/// <summary>One immutable version of the fee rules (task 017a).</summary>
/// <param name="Share">Share of the basis, 0 &lt; share ≤ 1 (MVP 0.15).</param>
/// <param name="SellerShareOfFee">For <see cref="FeePayer.Split"/> only: the seller's part of the fee, 0 &lt; part &lt; 1.</param>
public sealed record FeeScheduleDefinition(
    FeePayer Payer,
    FeeBasis Basis,
    decimal Share,
    decimal? SellerShareOfFee,
    Money? MinimumFee,
    Money? MaximumFee,
    VatTreatment VatTreatment,
    Currency Currency);

public sealed record FeeSchedule(int Version, FeeScheduleDefinition Definition, DateTimeOffset EffectiveFrom);

/// <summary>The fee one payer owes, rounded to minor units (ADR-008); VAT rounded separately.</summary>
public sealed record PayerFee(FeePayer Payer, Money Net, Money Vat);

/// <summary>The platform fee for one purchase price, snapshotted on the offer (financial-model.md §4).</summary>
public sealed record FeeQuote(Money Discount, IReadOnlyList<PayerFee> PerPayer, int ScheduleVersion, bool VatPending)
{
    public Money TotalNet => PerPayer.Select(p => p.Net).Aggregate((a, b) => a.Add(b));
}

/// <summary>Pure, deterministic fee calculation (no database).</summary>
public interface IFeeCalculator
{
    /// <exception cref="CurrencyMismatchException">The amounts or the schedule use different currencies.</exception>
    /// <exception cref="ArgumentException">INV-F1: the purchase price is not between zero and the face value.</exception>
    FeeQuote Calculate(Money faceValue, Money purchasePrice, FeeSchedule schedule);
}

public interface IFeeScheduleQuery
{
    /// <summary>The approved schedule in effect at <paramref name="at"/> (the latest effective_from not after it).</summary>
    Task<FeeSchedule> CurrentAsync(DateTimeOffset at, CancellationToken cancellationToken);

    Task<FeeSchedule?> GetAsync(int version, CancellationToken cancellationToken);
}
