using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Identity;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests;

/// <summary>
/// Task 023: every organization-scoped endpoint, found from the endpoint metadata, called by another seller and another
/// factor with the resources of a real deal, answers 403 or 404 (security.md §3: no existence leak, no foreign data).
/// </summary>
public sealed partial class MarketplaceTests
{
    /// <summary>Endpoints a foreign organization may call with success, and why.</summary>
    private static readonly Dictionary<string, string> CrossTenantAllowed = new(StringComparer.Ordinal)
    {
        ["GET /api/v1/marketplace/listings"] = "the feed of the caller's own visible listings",
        ["GET /api/v1/receivables"] = "the caller's own receivables",
        ["GET /api/v1/receivables/declaration"] = "the caller's own declaration text",
        ["GET /api/v1/receivables/export"] = "the caller's own receivables",
        ["GET /api/v1/deals"] = "the caller's own deals",
        ["GET /api/v1/deals/export"] = "the caller's own deals",
        ["GET /api/v1/offers"] = "the caller's own offers",
        ["GET /api/v1/listings"] = "the caller's own listings",
        ["GET /api/v1/fees/charges"] = "the caller's own charges",
        ["GET /api/v1/fees/statements/{period}"] = "the caller's own statement",
        ["GET /api/v1/dashboards/seller"] = "the caller's own figures",
        ["GET /api/v1/dashboards/factor"] = "the caller's own figures",
    };

    [Fact]
    public async Task Every_organization_scoped_endpoint_refuses_another_organizations_resources()
    {
        var (seller, factor, dealId, receivableId) = await SignedDealAsync();
        var listingId = await _database.ScalarAsync<Guid>($"SELECT listing_id FROM deals.deals WHERE id = '{dealId}'");
        var offerId = await _database.ScalarAsync<Guid>($"SELECT offer_id FROM deals.deals WHERE id = '{dealId}'");
        var documentId = await _database.ScalarAsync<Guid>($"SELECT TOP 1 id FROM documents.stored_documents WHERE owner_org_id = '{seller.Org}'");
        var disputeId = (await ApiClient.ReadAsync(await Send(factor, HttpMethod.Post, $"/api/v1/deals/{dealId}/disputes", new { kind = "Other", description = "x" })))
            .GetProperty("dispute").GetProperty("id").GetGuid();
        var outsiders = new[] { await SellerAsync(), await FactorAsync() };
        foreach (var outsider in outsiders)
        {
            await outsider.Client.StepUpAsync();
        }

        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<OrganizationScopedMetadata>() is not null && !(e.RoutePattern.RawText ?? string.Empty).Contains("/dev/", StringComparison.Ordinal))
            .ToList();
        var failures = new List<string>();
        var checkedCount = 0;
        foreach (var endpoint in endpoints)
        {
            var template = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                var key = $"{method} {Regex.Replace(template, @"\{(\w+)(:[^}]*)?\}", "{$1}").TrimEnd('/')}";
                var path = Regex.Replace(template, @"\{(\w+)(:[^}]*)?\}", m => Fill(template, m.Groups[1].Value, seller.Org, dealId, receivableId, listingId, offerId, disputeId, documentId));
                foreach (var outsider in outsiders)
                {
                    using var response = await SendAsOutsiderAsync(outsider, method, path);
                    checkedCount++;
                    var status = (int)response.StatusCode;
                    // 400 is accepted: the body is validated before the resource is looked up, so it reveals nothing about it.
                    var refused = status is 403 or 404 or 405;
                    if (!refused && !(CrossTenantAllowed.ContainsKey(key) && response.IsSuccessStatusCode) && status != 400)
                    {
                        failures.Add($"{key} as {(outsider == outsiders[0] ? "seller" : "factor")}: {status}");
                    }
                }
            }
        }

        Assert.True(checkedCount > 100, $"Only {checkedCount} calls were made.");
        Assert.True(failures.Count == 0, "Cross-tenant leaks:\n" + string.Join("\n", failures));
    }

    [Fact]
    public async Task Every_admin_endpoint_is_hidden_from_customers()
    {
        var customer = await SellerAsync();
        await customer.Client.StepUpAsync();
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).Contains("/admin/", StringComparison.Ordinal))
            .ToList();
        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var path = Regex.Replace("/" + endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{(\w+)(:[^}]*)?\}", m => m.Groups[2].Value.Contains("int", StringComparison.Ordinal) ? "1" : Guid.NewGuid().ToString());
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                using var response = await SendAsOutsiderAsync(customer, method, path);
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    failures.Add($"{method} {path}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.True(endpoints.Count > 30, $"Only {endpoints.Count} admin endpoints found.");
        Assert.True(failures.Count == 0, "Admin endpoints reachable by a customer:\n" + string.Join("\n", failures));
    }

    private static string Fill(string template, string name, Guid sellerOrg, Guid deal, Guid receivable, Guid listing, Guid offer, Guid dispute, Guid document)
    {
        if (name == "period")
        {
            return DateTime.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (name != "id")
        {
            return Guid.NewGuid().ToString();
        }

        var id = template switch
        {
            _ when template.Contains("/organizations/", StringComparison.Ordinal) => sellerOrg,
            _ when template.Contains("/deals/", StringComparison.Ordinal) => deal,
            _ when template.Contains("/receivables/", StringComparison.Ordinal) => receivable,
            _ when template.Contains("/listings/", StringComparison.Ordinal) => listing,
            _ when template.Contains("/offers/", StringComparison.Ordinal) => offer,
            _ when template.Contains("/disputes/", StringComparison.Ordinal) => dispute,
            _ when template.Contains("/documents/", StringComparison.Ordinal) => document,
            _ => Guid.NewGuid(),
        };
        return id.ToString();
    }

    private static async Task<HttpResponseMessage> SendAsOutsiderAsync(Party outsider, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(OrganizationScopedMetadata.Header, outsider.Org.ToString());
        if (method != "GET")
        {
            request.Headers.Add(IdentityModule.AntiforgeryHeader, await outsider.Client.AntiforgeryTokenAsync());
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            request.Content = JsonContent.Create(new { });
        }

        return await outsider.Client.Http.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
