using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modules.Deals.Contracts;
using Modules.Fees;
using Platform.Integration.Tests.Infrastructure;
using Shared.Kernel;

namespace Platform.Integration.Tests;

/// <summary>Task 018: fee charges from DealFunded, Finance actions, statements and the export.</summary>
public sealed partial class MarketplaceTests
{
    private static string ThisMonth => DateTime.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_funded_deal_gets_exactly_one_charge_per_payer_even_when_the_event_arrives_twice()
    {
        var (seller, factor, dealId) = await FundedDealAsync();
        var otherFactor = await FactorAsync();

        await DeliverDealFundedAsync();
        await DeliverDealFundedAsync();
        var charges = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/charges"))).EnumerateArray().ToList();
        var sellerCharges = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, "/api/v1/fees/charges"))).EnumerateArray().ToList();
        var othersCharges = (await ApiClient.ReadAsync(await Send(otherFactor, HttpMethod.Get, "/api/v1/fees/charges"))).EnumerateArray().ToList();
        var statement = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/fees/statements/{ThisMonth}"));
        var badPeriod = await ApiClient.ErrorCodeAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/statements/2026-13"));

        var charge = Assert.Single(charges);
        Assert.Equal("7500.00", charge.GetProperty("amount").GetProperty("amount").GetString());
        Assert.Equal("Due", charge.GetProperty("status").GetString());
        Assert.True(charge.GetProperty("vatPending").GetBoolean());
        Assert.Equal(dealId, charge.GetProperty("dealId").GetGuid());
        Assert.Empty(sellerCharges);
        Assert.Empty(othersCharges);
        Assert.Equal("7500.00", statement.GetProperty("totals")[0].GetProperty("total").GetProperty("amount").GetString());
        Assert.Equal("validation_failed", badPeriod);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM fees.fee_charges WHERE deal_id = '{dealId}'"));
    }

    [Fact]
    public async Task A_pending_reversal_can_be_withdrawn_and_the_charge_reversed_again_but_not_an_effective_one()
    {
        var (_, factor, _) = await FundedDealAsync();
        await DeliverDealFundedAsync();
        var chargeId = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/charges")))[0].GetProperty("id").GetGuid();
        using var finance = await StaffAsync("Finance");
        using var second = await StaffAsync("Finance");
        await finance.StepUpAsync();
        await second.StepUpAsync();

        var first = (await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/reverse", new { reason = "Greška" }))).GetProperty("id").GetGuid();
        var withdrawn = await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{first}/withdraw-reversal", new { reason = "Pogrešno predloženo" }));
        var again = (await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/reverse", new { reason = "Ispravno" }))).GetProperty("id").GetGuid();
        await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/fees/charges/{again}/approve-reversal"));
        var effective = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{again}/withdraw-reversal", new { reason = "Pogrešno predloženo" }));
        var notAReversal = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/withdraw-reversal", new { reason = "Pogrešno predloženo" }));

        Assert.Equal("Withdrawn", withdrawn.GetProperty("status").GetString());
        Assert.NotEqual(first, again);
        Assert.Equal("conflict_state", effective);
        Assert.Equal("not_found", notAReversal);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'FeeCharge.ReversalWithdrawn'"));
    }

    [Fact]
    public async Task Finance_invoices_records_payment_reverses_with_four_eyes_and_exports_the_month()
    {
        var (_, factor, _) = await FundedDealAsync();
        await DeliverDealFundedAsync();
        var chargeId = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/charges")))[0].GetProperty("id").GetGuid();
        using var finance = await StaffAsync("Finance");
        using var second = await StaffAsync("Finance");
        await finance.StepUpAsync();
        await second.StepUpAsync();

        using var byFactor = await factor.Client.GetAsync("/api/v1/admin/fees/charges");
        var paidEarly = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/paid", new { paidOn = Today, reference = "izvod 1" }));
        var invoiced = await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/invoiced", new { externalInvoiceRef = "SEF-2026-77" }));
        var paid = await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/paid", new { paidOn = Today, reference = "izvod 1" }));
        var reversal = await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/reverse", new { reason = "Greška u obračunu" }));
        var reversalId = reversal.GetProperty("id").GetGuid();
        var beforeApproval = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/fees/statements/{ThisMonth}"));
        var self = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{reversalId}/approve-reversal"));
        var approved = await ApiClient.ReadAsync(await second.PostAsync($"/api/v1/admin/fees/charges/{reversalId}/approve-reversal"));
        var twice = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/reverse", new { reason = "Opet" }));
        var afterApproval = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/fees/statements/{ThisMonth}"));
        using var export = await finance.GetAsync($"/api/v1/admin/fees/export?period={ThisMonth}");
        var bytes = await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.NotFound, byFactor.StatusCode);
        Assert.Equal("conflict_state", paidEarly);
        Assert.Equal("Invoiced", invoiced.GetProperty("status").GetString());
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal("PendingApproval", reversal.GetProperty("status").GetString());
        Assert.Equal("7500.00", beforeApproval.GetProperty("totals")[0].GetProperty("net").GetProperty("amount").GetString());
        Assert.Equal("four_eyes_required", self);
        Assert.Equal("Effective", approved.GetProperty("status").GetString());
        Assert.Equal("conflict_state", twice);
        Assert.Equal("0.00", afterApproval.GetProperty("totals")[0].GetProperty("net").GetProperty("amount").GetString());
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Contains(";7500.00;", csv, StringComparison.Ordinal);
        Assert.Contains(";-7500.00;", csv, StringComparison.Ordinal);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'FeeExport.Downloaded'"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => _database.ExecuteAsync($"UPDATE fees.fee_charges SET amount = 1 WHERE id = '{chargeId}'"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => _database.ExecuteAsync($"DELETE FROM fees.fee_charges WHERE id = '{reversalId}'"));
    }

    [Fact]
    public async Task A_split_schedule_with_standard_vat_gives_each_payer_its_charge_and_the_seller_its_net_proceeds()
    {
        using var finance = await StaffAsync("Finance");
        using var second = await StaffAsync("Finance");
        await second.StepUpAsync();
        var effectiveFrom = DateTimeOffset.UtcNow.AddSeconds(3);
        var schedule = await ApiClient.ReadAsync(await finance.PostAsync("/api/v1/admin/fee-schedules", new
        {
            definition = new { payer = "Split", basis = "Discount", share = 0.15m, sellerShareOfFee = 0.5m, vatTreatment = "Standard20", currency = "RSD" },
            effectiveFrom,
        }));
        using (var approved = await second.PostAsync($"/api/v1/admin/fee-schedules/{schedule.GetProperty("version").GetInt32()}/approve"))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await Task.Delay(effectiveFrom - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        var (seller, factor, dealId) = await FundedDealAsync();
        await DeliverDealFundedAsync();

        var deal = (await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/deals/{dealId}"))).GetProperty("deal");
        var factorCharge = Assert.Single((await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/charges"))).EnumerateArray());
        var sellerCharge = Assert.Single((await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, "/api/v1/fees/charges"))).EnumerateArray());
        var sellerStatement = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, $"/api/v1/fees/statements/{ThisMonth}"));

        Assert.Equal("1146250.00", deal.GetProperty("sellerNetProceeds").GetProperty("amount").GetString());
        Assert.Equal("3750.00", factorCharge.GetProperty("amount").GetProperty("amount").GetString());
        Assert.Equal("750.00", factorCharge.GetProperty("vat").GetProperty("amount").GetString());
        Assert.False(factorCharge.GetProperty("vatPending").GetBoolean());
        Assert.Equal("Seller", sellerCharge.GetProperty("payerRole").GetString());
        Assert.Equal("3750.00", sellerCharge.GetProperty("amount").GetProperty("amount").GetString());
        Assert.Equal("4500.00", sellerStatement.GetProperty("totals")[0].GetProperty("total").GetProperty("amount").GetString());
        Assert.Equal(2, await CountAsync($"SELECT COUNT(*) FROM fees.fee_charges WHERE deal_id = '{dealId}'"));
    }

    [Fact]
    public async Task A_reversed_charge_is_never_invoiced()
    {
        var (_, factor, _) = await FundedDealAsync();
        await DeliverDealFundedAsync();
        var chargeId = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/fees/charges")))[0].GetProperty("id").GetGuid();
        using var finance = await StaffAsync("Finance");
        await finance.StepUpAsync();
        using (var reversed = await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/reverse", new { reason = "Pogrešan posao" }))
        {
            Assert.Equal(HttpStatusCode.Created, reversed.StatusCode);
        }

        var invoiced = await ApiClient.ErrorCodeAsync(await finance.PostAsync($"/api/v1/admin/fees/charges/{chargeId}/invoiced", new { externalInvoiceRef = "SEF-1" }));

        Assert.Equal("charge_reversed", invoiced);
    }

    /// <summary>A Funded deal (the seller confirmed receipt), with its DealFunded event in the outbox.</summary>
    private async Task<(Party Seller, Party Factor, Guid DealId)> FundedDealAsync()
    {
        var (seller, factor, dealId, _) = await SignedDealAsync();
        var account = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, $"/api/v1/deals/{dealId}/funding-accounts")))[0].GetProperty("id").GetGuid();
        using (var reported = await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-report",
            new { amount = new { amount = "1150000.00", currency = "RSD" }, valueDate = Today, paymentReference = "97-9", payeeBankAccountId = account }))
        {
            Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        }

        using var funded = await Send(seller, HttpMethod.Post, $"/api/v1/deals/{dealId}/funding-confirmation", new { received = true });
        Assert.Equal(HttpStatusCode.OK, funded.StatusCode);
        return (seller, factor, dealId);
    }

    /// <summary>Delivers the DealFunded event to the fee consumer, as the worker's outbox dispatcher does.</summary>
    private async Task DeliverDealFundedAsync()
    {
        var payload = await _database.ScalarAsync<string>("SELECT TOP 1 payload FROM platform.outbox WHERE type = 'Deals.DealFunded'");
        var eventId = await _database.ScalarAsync<Guid>("SELECT TOP 1 id FROM platform.outbox WHERE type = 'Deals.DealFunded'");
        var integrationEvent = JsonSerializer.Deserialize<DealFunded>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web).AddKernelConverters())!;
        await using var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Shared.Infrastructure.Persistence.UnitOfWork>();
        var consumer = scope.ServiceProvider.GetRequiredService<FeeChargeConsumer>();
        await unitOfWork.ExecuteInTransactionAsync(ct => consumer.HandleAsync(integrationEvent, eventId, ct), TestContext.Current.CancellationToken);
    }
}
