using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Deals.Contracts;
using Modules.Marketplace.Contracts;
using Modules.Notifications;
using Modules.Notifications.Contracts;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

/// <summary>Task 019: domain events become notifications for the right members, with preferences and safe emails.</summary>
public sealed class NotificationTests(SqlServerContainer server) : IAsyncLifetime
{
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
    public async Task A_deal_event_notifies_the_members_of_both_parties_once_even_when_delivered_twice()
    {
        var (seller, sellerOrg, sellerEmail) = await OrganizationAsync();
        var (factor, factorOrg, factorEmail) = await FactorAsync();
        var (outsider, _, _) = await OrganizationAsync();
        var dealId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var created = new DealCreated(dealId, "DL-2026-000123", Guid.NewGuid(), sellerOrg, factorOrg);

        await HandleAsync(c => c.HandleAsync(created, eventId, TestContext.Current.CancellationToken));
        await HandleAsync(c => c.HandleAsync(created, eventId, TestContext.Current.CancellationToken));
        var sellerList = await ApiClient.ReadAsync(await seller.GetAsync("/api/v1/notifications"));
        var factorUnread = await ApiClient.ReadAsync(await factor.GetAsync("/api/v1/notifications/unread-count"));
        var outsiderList = await ApiClient.ReadAsync(await outsider.GetAsync("/api/v1/notifications"));
        var id = sellerList.GetProperty("items")[0].GetProperty("id").GetGuid();
        using var foreignRead = await outsider.PostAsync($"/api/v1/notifications/{id}/read");
        using var read = await seller.PostAsync($"/api/v1/notifications/{id}/read");
        var afterRead = await ApiClient.ReadAsync(await seller.GetAsync("/api/v1/notifications/unread-count"));

        Assert.Equal(2, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE event_id = '{eventId}'"));
        Assert.Equal("deal_action_required", sellerList.GetProperty("items")[0].GetProperty("type").GetString());
        Assert.Equal($"/deals/{dealId}", sellerList.GetProperty("items")[0].GetProperty("link").GetString());
        Assert.Equal(1, factorUnread.GetProperty("unread").GetInt32());
        Assert.Empty(outsiderList.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(0, afterRead.GetProperty("unread").GetInt32());

        await using var scope = Scope();
        await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendPendingAsync(TestContext.Current.CancellationToken);
        var email = _database.Emails.Sent.Last(m => m.To == sellerEmail);
        Assert.Equal("Cesio: Potrebna je vaša radnja na poslu", email.Subject);
        Assert.StartsWith("Potrebna je vaša radnja na poslu DL-2026-000123. Detalje pogledajte u aplikaciji Cesio: ", email.Body, StringComparison.Ordinal);
        Assert.Contains(_database.Emails.Sent, m => m.To == factorEmail && m.Subject == email.Subject);
    }

    [Fact]
    public async Task Optional_types_follow_the_preferences_and_mandatory_ones_cannot_be_turned_off()
    {
        var (seller, sellerOrg, sellerEmail) = await OrganizationAsync();

        var mandatory = await ApiClient.ErrorCodeAsync(await seller.SendAsync(HttpMethod.Put, "/api/v1/notification-preferences",
            new { items = new[] { new { type = "deal_action_required", channel = "email", enabled = false } } }));
        var saved = await ApiClient.ReadAsync(await seller.SendAsync(HttpMethod.Put, "/api/v1/notification-preferences",
            new { language = "en", items = new[] { new { type = "offer_received", channel = "email", enabled = false } } }));
        await HandleAsync(c => c.HandleAsync(new OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sellerOrg), Guid.NewGuid(), TestContext.Current.CancellationToken));
        await HandleAsync(c => c.HandleAsync(new DealCancelled(Guid.NewGuid(), "x", sellerOrg, Guid.NewGuid()), Guid.NewGuid(), TestContext.Current.CancellationToken));
        var before = _database.Emails.Sent.Count;
        await using (var scope = Scope())
        {
            await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendPendingAsync(TestContext.Current.CancellationToken);
        }

        var mails = _database.Emails.Sent.Skip(before).Where(m => m.To == sellerEmail).ToList();

        Assert.Equal("notification_mandatory", mandatory);
        Assert.Equal("en", saved.GetProperty("language").GetString());
        Assert.Equal(2, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE org_id = '{sellerOrg}'"));
        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE org_id = '{sellerOrg}' AND type = 'offer_received' AND email_status = 'Skipped'"));
        Assert.Contains(mails, m => m.Subject == "Cesio: A deal was cancelled");
        Assert.DoesNotContain(mails, m => m.Subject.Contains("offer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Security_events_also_reach_the_official_email_and_digests_group_new_listings()
    {
        var (_, sellerOrg, _) = await OrganizationAsync();
        var official = $"apr-{Guid.NewGuid():N}@firma.rs";
        await _database.ExecuteAsync($"UPDATE orgs.organizations SET official_email = '{official}' WHERE id = '{sellerOrg}'");
        var (_, factorOrg, factorEmail) = await FactorAsync();

        await HandleAsync(c => c.HandleAsync(new Modules.Organizations.Contracts.SefConnectionInvalidated(sellerOrg), Guid.NewGuid(), TestContext.Current.CancellationToken));
        await HandleAsync(c => c.HandleAsync(new OrganizationNotificationRequested(factorOrg, NotificationTypes.NewListing, "/marketplace/1", null), Guid.NewGuid(), TestContext.Current.CancellationToken));
        await HandleAsync(c => c.HandleAsync(new OrganizationNotificationRequested(factorOrg, NotificationTypes.NewListing, "/marketplace/2", null), Guid.NewGuid(), TestContext.Current.CancellationToken));
        int digests;
        await using (var scope = Scope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<EmailDispatcher>();
            await dispatcher.SendPendingAsync(TestContext.Current.CancellationToken);
            digests = await dispatcher.SendDigestsAsync(TestContext.Current.CancellationToken);
        }

        Assert.Contains(_database.Emails.Sent, m => m.To == official && m.Subject == "Cesio: Bezbednosno obaveštenje");
        Assert.Equal(1, digests);
        Assert.Single(_database.Emails.Sent, m => m.To == factorEmail && m.Body.StartsWith("Broj novih oglasa po vašim filterima: 2.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failing_email_is_retried_three_times_and_then_shown_as_failed()
    {
        await using var failing = new ApiApp(_database, configureServices: services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FailingEmailSender>();
        });
        var (_, org, _) = await OrganizationAsync();
        await HandleAsync(c => c.HandleAsync(new DealCancelled(Guid.NewGuid(), "x", org, Guid.NewGuid()), Guid.NewGuid(), TestContext.Current.CancellationToken));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var scope = failing.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendPendingAsync(TestContext.Current.CancellationToken);
            await _database.ExecuteAsync("UPDATE notifications.notifications SET next_email_at = DATEADD(minute, -1, SYSDATETIMEOFFSET()) WHERE email_status = 'Pending'");
        }

        var operations = _app.NewClient();
        await operations.SignUpAsync();
        using (var grant = await operations.PostAsync("/api/v1/dev/me/platform-roles", new { role = "Operations" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        }

        var failures = await ApiClient.ReadAsync(await operations.GetAsync("/api/v1/admin/notification-failures"));
        var (customer, _, _) = await OrganizationAsync();
        using var hidden = await customer.GetAsync("/api/v1/admin/notification-failures");

        Assert.Equal(1, await _database.ScalarAsync<int>($"SELECT COUNT(*) FROM notifications.notifications WHERE org_id = '{org}' AND email_status = 'Failed' AND email_attempts = 3 AND last_error = 'InvalidOperationException'"));
        Assert.Contains(failures.EnumerateArray(), f => f.GetProperty("organizationId").GetGuid() == org);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    // ----- helpers -----

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => throw new InvalidOperationException("smtp down for someone@example.rs");
    }

    private async Task<(ApiClient Client, Guid Org, string Email)> OrganizationAsync()
    {
        var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/organizations", new { pib = TestIds.NewPib() }))).GetProperty("id").GetGuid();
        return (client, org, email);
    }

    private async Task<(ApiClient Client, Guid Org, string Email)> FactorAsync()
    {
        var client = _app.NewClient();
        var (email, _) = await client.SignUpAsync();
        var org = (await ApiClient.ReadAsync(await client.PostAsync("/api/v1/dev/factor-organizations", new { pib = TestIds.NewPib(), legalName = "Faktor a.d." }))).GetProperty("id").GetGuid();
        return (client, org, email);
    }

    private AsyncServiceScope Scope()
    {
        var scope = _app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        return scope;
    }

    private async Task HandleAsync(Func<NotificationConsumers, Task> handle)
    {
        await using var scope = Scope();
        var consumer = scope.ServiceProvider.GetRequiredService<NotificationConsumers>();
        await scope.ServiceProvider.GetRequiredService<UnitOfWork>().ExecuteInTransactionAsync(_ => handle(consumer), TestContext.Current.CancellationToken);
    }
}
