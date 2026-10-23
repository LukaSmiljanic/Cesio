using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Modules.Fees.Contracts;
using Platform.Integration.Tests.Infrastructure;

namespace Platform.Integration.Tests;

/// <summary>Task 017a: versioned fee schedules, four-eyes approval and immutability.</summary>
public sealed class FeeScheduleTests(SqlServerContainer server) : IAsyncLifetime
{
    private static readonly object Definition = new
    {
        payer = "Factor",
        basis = "Discount",
        share = 0.2m,
        minimumFee = new { amount = "100.00", currency = "RSD" },
        vatTreatment = "TBD",
        currency = "RSD",
    };

    private readonly TestDatabase _database = new(server);
    private ApiApp _app = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _app = new ApiApp(_database);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task The_seed_is_fifteen_percent_of_the_discount_paid_by_the_factor()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var current = await scope.ServiceProvider.GetRequiredService<IFeeScheduleQuery>().CurrentAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        Assert.Equal(1, current.Version);
        Assert.Equal(0.15m, current.Definition.Share);
        Assert.Equal(FeePayer.Factor, current.Definition.Payer);
        Assert.Equal(VatTreatment.TBD, current.Definition.VatTreatment);
        Assert.Null(current.Definition.MinimumFee);
    }

    [Fact]
    public async Task A_new_version_needs_a_second_finance_user_and_takes_effect_at_its_date()
    {
        using var creator = await FinanceAsync();
        using var approver = await FinanceAsync();
        var effectiveFrom = DateTimeOffset.UtcNow.AddMinutes(5);

        var created = await ApiClient.ReadAsync(await creator.PostAsync("/api/v1/admin/fee-schedules", new { definition = Definition, effectiveFrom }));
        var version = created.GetProperty("version").GetInt32();
        await creator.StepUpAsync();
        var self = await ApiClient.ErrorCodeAsync(await creator.PostAsync($"/api/v1/admin/fee-schedules/{version}/approve"));
        var withoutStepUp = await ApiClient.ErrorCodeAsync(await approver.PostAsync($"/api/v1/admin/fee-schedules/{version}/approve"));
        await approver.StepUpAsync();
        var approved = await ApiClient.ReadAsync(await approver.PostAsync($"/api/v1/admin/fee-schedules/{version}/approve"));
        var again = await ApiClient.ErrorCodeAsync(await approver.PostAsync($"/api/v1/admin/fee-schedules/{version}/approve"));
        await using var scope = _app.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IFeeScheduleQuery>();
        var now = await query.CurrentAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var later = await query.CurrentAsync(effectiveFrom.AddSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(2, version);
        Assert.Equal("PendingApproval", created.GetProperty("status").GetString());
        Assert.Equal("four_eyes_required", self);
        Assert.Equal("step_up_required", withoutStepUp);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("conflict_state", again);
        Assert.Equal(1, now.Version);
        Assert.Equal(2, later.Version);
        Assert.Equal(Shared.Kernel.Money.Parse("100.00", Shared.Kernel.Currency.Rsd), later.Definition.MinimumFee);
        Assert.Equal(1, await _database.ScalarAsync<int>("SELECT COUNT(*) FROM audit.audit_events WHERE action = 'FeeSchedule.Approved'"));
    }

    [Fact]
    public async Task Schedules_are_immutable_in_the_database()
    {
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("UPDATE fees.fee_schedules SET definition = '{}' WHERE version = 1"));
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync($"UPDATE fees.fee_schedules SET approved_by = '{Guid.NewGuid()}' WHERE version = 1"));
        await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("DELETE FROM fees.fee_schedules WHERE version = 1"));
    }

    [Fact]
    public async Task Invalid_definitions_and_non_finance_users_are_refused()
    {
        using var finance = await FinanceAsync();
        using var other = _app.NewClient();
        await other.SignUpAsync();
        var future = DateTimeOffset.UtcNow.AddDays(1);

        using var list = await other.GetAsync("/api/v1/admin/fee-schedules");
        using var create = await other.PostAsync("/api/v1/admin/fee-schedules", new { definition = Definition, effectiveFrom = future });
        var share = await ApiClient.ErrorCodeAsync(await finance.PostAsync("/api/v1/admin/fee-schedules",
            new { definition = new { payer = "Factor", basis = "Discount", share = 1.5m, vatTreatment = "TBD", currency = "RSD" }, effectiveFrom = future }));
        var split = await ApiClient.ErrorCodeAsync(await finance.PostAsync("/api/v1/admin/fee-schedules",
            new { definition = new { payer = "Split", basis = "Discount", share = 0.15m, vatTreatment = "TBD", currency = "RSD" }, effectiveFrom = future }));
        var euro = await ApiClient.ErrorCodeAsync(await finance.PostAsync("/api/v1/admin/fee-schedules",
            new { definition = new { payer = "Factor", basis = "Discount", share = 0.15m, vatTreatment = "TBD", currency = "EUR" }, effectiveFrom = future }));
        var past = await ApiClient.ErrorCodeAsync(await finance.PostAsync("/api/v1/admin/fee-schedules", new { definition = Definition, effectiveFrom = DateTimeOffset.UtcNow.AddDays(-1) }));
        var missing = await ApiClient.ErrorCodeAsync(await finance.PostAsync("/api/v1/admin/fee-schedules", new { effectiveFrom = future }));

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.All([share, split, euro, past, missing], code => Assert.Equal("validation_failed", code));
    }

    private async Task<ApiClient> FinanceAsync()
    {
        var client = _app.NewClient();
        await client.SignUpAsync();
        using var grant = await client.PostAsync("/api/v1/dev/me/platform-roles", new { role = "Finance" });
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        return client;
    }
}
