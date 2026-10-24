using System.Net;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 022: dashboards on a seeded deal, the parameters editor, audit search and the CSV exports.</summary>
public sealed partial class MarketplaceTests
{
    [Fact]
    public async Task The_dashboards_show_the_figures_of_a_funded_deal_and_only_to_their_own_party()
    {
        var (seller, factor, _) = await FundedDealAsync();
        await DeliverDealFundedAsync();

        var sellerBoard = await ApiClient.ReadAsync(await Send(seller, HttpMethod.Get, "/api/v1/dashboards/seller"));
        var factorBoard = await ApiClient.ReadAsync(await Send(factor, HttpMethod.Get, "/api/v1/dashboards/factor"));
        using var sellerAsFactor = await Send(seller, HttpMethod.Get, "/api/v1/dashboards/factor");
        using var factorAsSeller = await Send(factor, HttpMethod.Get, "/api/v1/dashboards/seller");
        using var operations = await StaffAsync("Operations");
        var admin = await ApiClient.ReadAsync(await operations.GetAsync("/api/v1/admin/dashboard"));
        using var customerAdmin = await seller.Client.GetAsync("/api/v1/admin/dashboard");

        Assert.Equal("1150000.00", sellerBoard.GetProperty("fundedThisMonth")[0].GetProperty("amount").GetString());
        Assert.Equal(0.0417m, sellerBoard.GetProperty("averageDiscountRate").GetDecimal());
        Assert.Equal(1, sellerBoard.GetProperty("receivablesByState").GetProperty("Funded").GetInt32());
        Assert.Equal(1m, factorBoard.GetProperty("winRate").GetDecimal());
        Assert.Equal(1, factorBoard.GetProperty("portfolio").EnumerateArray().Sum(b => b.GetProperty("count").GetInt32()));
        Assert.NotEmpty(factorBoard.GetProperty("feesOutstanding").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, sellerAsFactor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, factorAsSeller.StatusCode);
        Assert.True(admin.GetProperty("month").GetProperty("funded").GetInt32() >= 1);
        Assert.True(admin.GetProperty("integrationHealth").GetProperty("jobs").GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.NotFound, customerAdmin.StatusCode);
    }

    [Fact]
    public async Task A_parameter_change_is_a_new_version_and_a_money_parameter_needs_a_second_person()
    {
        using var admin = await StaffAsync("PlatformAdmin");
        using var finance = await StaffAsync("Finance");
        using var noStepUp = await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/parameters/marketplace.maxListingDays", new { value = 20 });
        await admin.StepUpAsync();
        await finance.StepUpAsync();

        var changed = await ApiClient.ReadAsync(await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/parameters/marketplace.maxListingDays", new { value = 20 }));
        var again = await ApiClient.ReadAsync(await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/parameters/marketplace.maxListingDays", new { value = 25 }));
        using var proposed = await admin.SendAsync(HttpMethod.Put, "/api/v1/admin/parameters/receivables.minFaceValue", new { value = 50000 });
        var proposal = await ApiClient.ReadAsync(proposed);
        var self = await ApiClient.ErrorCodeAsync(await admin.PostAsync($"/api/v1/admin/parameters/proposals/{proposal.GetProperty("id").GetGuid()}/approve"));
        var approved = await ApiClient.ReadAsync(await finance.PostAsync($"/api/v1/admin/parameters/proposals/{proposal.GetProperty("id").GetGuid()}/approve"));
        var history = (await ApiClient.ReadAsync(await admin.GetAsync("/api/v1/admin/parameters/marketplace.maxListingDays"))).EnumerateArray().ToList();
        using var operations = await StaffAsync("Operations");
        using var hidden = await operations.GetAsync("/api/v1/admin/parameters");

        Assert.Equal(HttpStatusCode.Unauthorized, noStepUp.StatusCode);
        Assert.Equal(changed.GetProperty("version").GetInt32() + 1, again.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Accepted, proposed.StatusCode);
        Assert.Equal("four_eyes_required", self);
        Assert.Equal("50000", approved.GetProperty("value").GetString());
        Assert.Equal(2, history.Count);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'BusinessParameter.Approved'"));
    }

    [Fact]
    public async Task Auditors_search_and_export_the_audit_log_and_parties_export_their_own_rows()
    {
        var (seller, factor, dealId) = await FundedDealAsync();
        using var auditor = await StaffAsync("Auditor");
        await auditor.StepUpAsync();

        var found = await ApiClient.ReadAsync(await auditor.GetAsync($"/api/v1/admin/audit?entityType=Deal&entityId={dealId}"));
        using var export = await auditor.GetAsync($"/api/v1/admin/audit/export?entityId={dealId}");
        var exportText = await export.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var customer = await seller.Client.GetAsync("/api/v1/admin/audit");
        using var deals = await Send(factor, HttpMethod.Get, "/api/v1/deals/export");
        var dealsCsv = await deals.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var receivables = await Send(seller, HttpMethod.Get, "/api/v1/receivables/export");
        var receivablesCsv = await receivables.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var outsider = await SellerAsync();
        using var outsiderDeals = await Send(outsider, HttpMethod.Get, "/api/v1/deals/export");
        var outsiderCsv = await outsiderDeals.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(found.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "Deal.Funded");
        Assert.DoesNotContain(found.GetProperty("items").EnumerateArray(), e => e.TryGetProperty("ipAddress", out _));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("Deal.Funded", exportText, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
        Assert.Contains(";Factor;Funded;", dealsCsv, StringComparison.Ordinal);
        Assert.Contains(";Funded;", receivablesCsv, StringComparison.Ordinal);
        Assert.Single(outsiderCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.True(await CountAsync("SELECT COUNT(*) FROM audit.access_log WHERE action IN ('Audit.Exported', 'Deals.Exported', 'Receivables.Exported')") >= 3);
    }
}
