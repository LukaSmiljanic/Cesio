using Modules.Organizations.Contracts;
using Modules.Receivables;
using Modules.Receivables.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 011: the checks that need no database, each at its boundaries (parameters at their defaults).</summary>
public sealed class ReceivableCheckTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly Guid SellerId = Guid.NewGuid();

    [Theory]
    [InlineData(15, "Pass")]
    [InlineData(14, "Fail")]
    public async Task Not_matured_needs_the_minimum_days_to_maturity(int days, string expected) =>
        Assert.Equal(expected, (await new NotMaturedCheck(new Defaults()).RunAsync(Context(maturity: Today.AddDays(days)), TestContext.Current.CancellationToken)).Result.ToString());

    [Theory]
    [InlineData(180, "Pass")]
    [InlineData(181, "Fail")]
    public async Task Max_tenor_limits_the_days_to_maturity(int days, string expected) =>
        Assert.Equal(expected, (await new MaxTenorCheck(new Defaults()).RunAsync(Context(maturity: Today.AddDays(days)), TestContext.Current.CancellationToken)).Result.ToString());

    [Theory]
    [InlineData("499999.99", "Fail")]
    [InlineData("500000.00", "Pass")]
    [InlineData("200000000.00", "Pass")]
    [InlineData("200000000.01", "Fail")]
    public async Task Amount_limits_are_inclusive(string amount, string expected) =>
        Assert.Equal(expected, (await new AmountLimitsCheck(new Defaults()).RunAsync(Context(faceValue: Money.Parse(amount, Currency.Rsd)), TestContext.Current.CancellationToken)).Result.ToString());

    [Fact]
    public async Task Amount_limits_fail_for_a_currency_without_limits() =>
        Assert.Equal(CheckResult.Fail, (await new AmountLimitsCheck(new Defaults()).RunAsync(Context(faceValue: new Money(1_000_000m, Currency.Eur), gross: 1_000_000m, currency: Currency.Eur), TestContext.Current.CancellationToken)).Result);

    [Theory]
    [InlineData("1200000.00", "Pass")]
    [InlineData("1200000.01", "Fail")]
    public async Task The_face_value_cannot_exceed_the_invoice_gross(string amount, string expected) =>
        Assert.Equal(expected, (await new FaceValueConsistentCheck().RunAsync(Context(faceValue: Money.Parse(amount, Currency.Rsd)), TestContext.Current.CancellationToken)).Result.ToString());

    [Fact]
    public async Task The_debtor_cannot_be_the_seller()
    {
        Assert.Equal(CheckResult.Pass, (await new DebtorNotSellerCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Fail, (await new DebtorNotSellerCheck().RunAsync(Context(debtorPib: "101134702"), TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task The_declaration_must_be_complete()
    {
        Assert.Equal(CheckResult.Pass, (await new DeclarationCompleteCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Fail, (await new DeclarationCompleteCheck().RunAsync(Context(declarationComplete: false), TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task The_seller_must_be_approved_without_a_hold_and_the_issuer_of_the_invoice()
    {
        Assert.Equal(CheckResult.Pass, (await new SellerApprovedCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Fail, (await new SellerApprovedCheck().RunAsync(Context(onHold: true), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Fail, (await new SellerApprovedCheck().RunAsync(Context(status: OnboardingStatus.Suspended), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Pass, (await new SellerIsIssuerCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
    }

    [Fact]
    public async Task Only_a_fresh_approved_sef_status_passes_and_screening_and_related_parties_are_stubs()
    {
        Assert.Equal(CheckResult.Pass, (await new SefFreshApprovedCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Fail, (await new SefFreshApprovedCheck().RunAsync(Context(sefStatus: "Storno"), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Warn, (await new DebtorScreeningCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CheckResult.Pass, (await new RelatedPartyCheck().RunAsync(Context(), TestContext.Current.CancellationToken)).Result);
    }

    private static CheckContext Context(
        DateOnly? maturity = null,
        Money? faceValue = null,
        decimal gross = 1_200_000m,
        Currency? currency = null,
        string debtorPib = "106006802",
        bool declarationComplete = true,
        bool onHold = false,
        OnboardingStatus status = OnboardingStatus.Approved,
        string sefStatus = "Approved")
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            SellerOrgId = SellerId,
            SefInvoiceId = "1",
            InvoiceNumber = "F-1",
            IssueDate = Today,
            DueDate = maturity ?? Today.AddDays(60),
            Currency = currency ?? Currency.Rsd,
            GrossAmount = gross,
            UblSha256 = "x",
            SefStatus = sefStatus,
        };
        var receivable = new Receivable
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            SellerOrgId = SellerId,
            FaceValue = faceValue ?? new Money(gross, invoice.Currency),
            MaturityDate = invoice.DueDate,
            State = ReceivableState.PendingVerification,
            Fingerprint = "x",
        };
        return new CheckContext(
            receivable,
            invoice,
            new Debtor { Pib = debtorPib, LegalName = "Kupac d.o.o." },
            new OrganizationSummary(SellerId, OrganizationType.Seller, status, "101134702", "Prodavac d.o.o."),
            onHold,
            sefStatus,
            declarationComplete,
            Today);
    }

    private sealed class Defaults : IBusinessParameters
    {
        public Task<T> GetAsync<T>(string key, T defaultValue, CancellationToken cancellationToken) => Task.FromResult(defaultValue);
    }
}
