using System.Net;
using System.Text;
using Integrations.Sef;

namespace Platform.Integration.Tests;

/// <summary>SEF retry policy and UBL parsing (task 010, integrations.md §1).</summary>
public sealed class SefClientTests
{
    [Fact]
    public async Task Transient_failures_are_retried_and_then_succeed()
    {
        var inner = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var client = new HttpClient(new SefRetryHandler(TimeSpan.FromMilliseconds(1)) { InnerHandler = inner }) { BaseAddress = new Uri("https://sef.test/") };

        using var response = await client.GetAsync(new Uri("x", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Retries_stop_after_three_and_4xx_is_never_retried()
    {
        var failing = new SequenceHandler(Enumerable.Repeat(HttpStatusCode.BadGateway, 10).ToArray());
        using var client = new HttpClient(new SefRetryHandler(TimeSpan.FromMilliseconds(1)) { InnerHandler = failing }) { BaseAddress = new Uri("https://sef.test/") };
        var unauthorized = new SequenceHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var client401 = new HttpClient(new SefRetryHandler(TimeSpan.FromMilliseconds(1)) { InnerHandler = unauthorized }) { BaseAddress = new Uri("https://sef.test/") };

        using var exhausted = await client.GetAsync(new Uri("x", UriKind.Relative), TestContext.Current.CancellationToken);
        using var rejected = await client401.GetAsync(new Uri("x", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, exhausted.StatusCode);
        Assert.Equal(1 + SefRetryHandler.MaxRetries, failing.Calls);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal(1, unauthorized.Calls);
    }

    [Fact]
    public void A_sef_ubl_invoice_is_parsed_with_invariant_amounts()
    {
        var ubl = UblParser.Parse(FakeSefClient.Ubl("101134702", "106006802", "F-1", 1234.5m, 246.9m, issueDate: new DateOnly(2026, 9, 1), dueDate: new DateOnly(2026, 11, 30)));

        Assert.Equal("F-1", ubl.Number);
        Assert.Equal("101134702", ubl.Supplier.Pib);
        Assert.Equal("106006802", ubl.Buyer.Pib);
        Assert.Equal(1234.50m, ubl.NetAmount);
        Assert.Equal(246.90m, ubl.VatAmount);
        Assert.Equal(1481.40m, ubl.GrossAmount);
        Assert.Equal(new DateOnly(2026, 11, 30), ubl.DueDate);
        Assert.False(ubl.IsCreditNote);
    }

    [Fact]
    public void A_ubl_with_a_dtd_is_refused()
    {
        var xxe = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><Invoice>&e;</Invoice>");

        Assert.Throws<UblFormatException>(() => UblParser.Parse(xxe));
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls, statuses.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
