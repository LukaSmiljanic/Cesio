using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Integrations.Sef;

/// <summary>
/// In-memory SEF for tests and local development (configuration <c>Sef:Client = Fake</c>). Tests add invoices per key;
/// locally a key of the form <c>demo-{PIB}</c> sees three generated invoices issued by that PIB.
/// </summary>
public sealed class FakeSefClient : ISefClient
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, (string Status, byte[] Ubl)>> _invoices = new(StringComparer.Ordinal);
    private long _nextId = 100_000;

    /// <summary>Simulated failures: keys that answer 401, and the number of upcoming calls that fail as unavailable.</summary>
    public HashSet<string> RejectedKeys { get; } = new(StringComparer.Ordinal);

    public int FailNextCalls { get; set; }

    public long AddInvoice(string apiKey, byte[] ubl, string status = "Sent")
    {
        var id = Interlocked.Increment(ref _nextId);
        _invoices.GetOrAdd(apiKey, _ => new())[id] = (status, ubl);
        return id;
    }

    public Task CheckKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        Guard(apiKey);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<long>> ListSalesInvoiceIdsAsync(string apiKey, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        Guard(apiKey);
        IReadOnlyList<long> ids = [.. For(apiKey)
            .Where(i => UblParser.Parse(i.Value.Ubl).IssueDate is var date && date >= from && date <= to)
            .Select(i => i.Key)
            .Order()];
        return Task.FromResult(ids);
    }

    public Task<SefSalesInvoice?> GetSalesInvoiceAsync(string apiKey, long invoiceId, CancellationToken cancellationToken)
    {
        Guard(apiKey);
        return Task.FromResult(For(apiKey).TryGetValue(invoiceId, out var invoice) ? new SefSalesInvoice(invoiceId, invoice.Status, DateTimeOffset.UtcNow) : null);
    }

    public Task<byte[]> GetSalesInvoiceUblAsync(string apiKey, long invoiceId, CancellationToken cancellationToken)
    {
        Guard(apiKey);
        return For(apiKey).TryGetValue(invoiceId, out var invoice)
            ? Task.FromResult(invoice.Ubl)
            : throw new SefUnavailableException($"Invoice {invoiceId} has no UBL.");
    }

    /// <summary>A minimal SEF-style UBL invoice (amounts in invariant culture).</summary>
    public static byte[] Ubl(
        string supplierPib, string buyerPib, string number, decimal net, decimal vat, string currency = "RSD",
        DateOnly? issueDate = null, DateOnly? dueDate = null, string buyerName = "Kupac d.o.o.", decimal? gross = null)
    {
        var issue = issueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var due = dueDate ?? issue.AddDays(60);
        var xml = string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                     xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                     xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
              <cbc:ID>{number}</cbc:ID>
              <cbc:IssueDate>{issue:yyyy-MM-dd}</cbc:IssueDate>
              <cbc:DueDate>{due:yyyy-MM-dd}</cbc:DueDate>
              <cbc:InvoiceTypeCode>380</cbc:InvoiceTypeCode>
              <cbc:DocumentCurrencyCode>{currency}</cbc:DocumentCurrencyCode>
              <cac:AccountingSupplierParty><cac:Party>
                <cbc:EndpointID schemeID="9948">{supplierPib}</cbc:EndpointID>
                <cac:PartyTaxScheme><cbc:CompanyID>RS{supplierPib}</cbc:CompanyID></cac:PartyTaxScheme>
                <cac:PartyLegalEntity><cbc:RegistrationName>Prodavac d.o.o.</cbc:RegistrationName><cbc:CompanyID>12345678</cbc:CompanyID></cac:PartyLegalEntity>
              </cac:Party></cac:AccountingSupplierParty>
              <cac:AccountingCustomerParty><cac:Party>
                <cbc:EndpointID schemeID="9948">{buyerPib}</cbc:EndpointID>
                <cac:PartyTaxScheme><cbc:CompanyID>RS{buyerPib}</cbc:CompanyID></cac:PartyTaxScheme>
                <cac:PartyLegalEntity><cbc:RegistrationName>{buyerName}</cbc:RegistrationName><cbc:CompanyID>87654321</cbc:CompanyID></cac:PartyLegalEntity>
              </cac:Party></cac:AccountingCustomerParty>
              <cac:TaxTotal><cbc:TaxAmount currencyID="{currency}">{vat:0.00}</cbc:TaxAmount></cac:TaxTotal>
              <cac:LegalMonetaryTotal>
                <cbc:TaxExclusiveAmount currencyID="{currency}">{net:0.00}</cbc:TaxExclusiveAmount>
                <cbc:TaxInclusiveAmount currencyID="{currency}">{gross ?? (net + vat):0.00}</cbc:TaxInclusiveAmount>
                <cbc:PayableAmount currencyID="{currency}">{gross ?? (net + vat):0.00}</cbc:PayableAmount>
              </cac:LegalMonetaryTotal>
            </Invoice>
            """);
        return Encoding.UTF8.GetBytes(xml);
    }

    private ConcurrentDictionary<long, (string Status, byte[] Ubl)> For(string apiKey)
    {
        if (apiKey.StartsWith("demo-", StringComparison.Ordinal) && !_invoices.ContainsKey(apiKey))
        {
            var pib = apiKey["demo-".Length..];
            for (var i = 1; i <= 3; i++)
            {
                AddInvoice(apiKey, Ubl(pib, "106006802", $"DEMO-{i:000}", 1_000_000m * i, 200_000m * i, buyerName: $"Demo kupac {i} d.o.o."), "Approved");
            }
        }

        return _invoices.GetOrAdd(apiKey, _ => new());
    }

    private void Guard(string apiKey)
    {
        if (RejectedKeys.Contains(apiKey))
        {
            throw new SefUnauthorizedException();
        }

        if (FailNextCalls > 0)
        {
            FailNextCalls--;
            throw new SefUnavailableException("Simulated SEF outage.");
        }
    }
}
