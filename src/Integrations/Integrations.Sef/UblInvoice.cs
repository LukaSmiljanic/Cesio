using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Integrations.Sef;

/// <summary>The fields of a SEF UBL 2.1 invoice the platform imports (task 010).</summary>
public sealed record UblInvoice(
    bool IsCreditNote,
    string Number,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string Currency,
    UblParty Supplier,
    UblParty Buyer,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrossAmount,
    decimal? PayableAmount);

/// <param name="Pib">9 digits without the "RS" prefix, or null when the party has no Serbian PIB.</param>
public sealed record UblParty(string? Pib, string? Mb, string? Name);

public sealed class UblFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Parses UBL with <c>System.Xml</c> only: DTDs are prohibited and no external resources are resolved (no XXE).
/// Amounts are parsed as decimal with the invariant culture (ADR-008).
/// </summary>
public static class UblParser
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public static UblInvoice Parse(byte[] xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XDocument document;
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new UblFormatException("The UBL document is not well-formed XML.", exception);
        }

        var root = document.Root ?? throw new UblFormatException("The UBL document is empty.");
        var isCreditNote = root.Name.LocalName == "CreditNote" || Text(root, Cbc + "InvoiceTypeCode") == "381";
        var totals = root.Element(Cac + "LegalMonetaryTotal") ?? throw new UblFormatException("LegalMonetaryTotal is missing.");
        var currency = Required(root, Cbc + "DocumentCurrencyCode");

        // Only the VAT total in the document currency; a second TaxTotal in the tax currency must not be added to it.
        var vat = root.Elements(Cac + "TaxTotal")
            .Select(t => t.Element(Cbc + "TaxAmount"))
            .Where(a => a is not null && (a.Attribute("currencyID")?.Value ?? currency) == currency)
            .Select(a => Amount(a!, currency))
            .FirstOrDefault();

        return new UblInvoice(
            isCreditNote,
            Required(root, Cbc + "ID"),
            Date(Required(root, Cbc + "IssueDate")),
            Text(root, Cbc + "DueDate") is { } due ? Date(due)
                : root.Element(Cac + "PaymentMeans")?.Element(Cbc + "PaymentDueDate")?.Value is { } paymentDue ? Date(paymentDue) : null,
            currency,
            Party(root.Element(Cac + "AccountingSupplierParty")),
            Party(root.Element(Cac + "AccountingCustomerParty")),
            Amount(RequiredElement(totals, Cbc + "TaxExclusiveAmount"), currency),
            vat,
            Amount(RequiredElement(totals, Cbc + "TaxInclusiveAmount"), currency),
            totals.Element(Cbc + "PayableAmount") is { } payable ? Amount(payable, currency) : null);
    }

    private static UblParty Party(XElement? wrapper)
    {
        var party = wrapper?.Element(Cac + "Party") ?? throw new UblFormatException("A party is missing.");
        var taxId = party.Elements(Cac + "PartyTaxScheme").Select(s => s.Element(Cbc + "CompanyID")?.Value.Trim()).FirstOrDefault(v => v is not null);
        var pib = taxId is not null && taxId.StartsWith("RS", StringComparison.OrdinalIgnoreCase) ? taxId[2..] : taxId;
        if (pib is null && party.Element(Cbc + "EndpointID") is { } endpoint && endpoint.Attribute("schemeID")?.Value == "9948")
        {
            pib = endpoint.Value.Trim();
        }

        var legal = party.Element(Cac + "PartyLegalEntity");
        return new UblParty(
            pib is { Length: 9 } && pib.All(char.IsAsciiDigit) ? pib : null,
            legal?.Element(Cbc + "CompanyID")?.Value.Trim(),
            legal?.Element(Cbc + "RegistrationName")?.Value.Trim() ?? party.Element(Cac + "PartyName")?.Element(Cbc + "Name")?.Value.Trim());
    }

    private static string? Text(XElement parent, XName name) => parent.Element(name)?.Value.Trim();

    private static string Required(XElement parent, XName name) =>
        Text(parent, name) is { Length: > 0 } value ? value : throw new UblFormatException($"{name.LocalName} is missing.");

    private static DateOnly Date(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new UblFormatException($"'{value}' is not a date (yyyy-MM-dd).");

    private static XElement RequiredElement(XElement parent, XName name) =>
        parent.Element(name) ?? throw new UblFormatException($"{name.LocalName} is missing.");

    /// <summary>
    /// An amount in the document currency with at most 2 decimals and 15 integer digits (it must fit decimal(19,4)
    /// without rounding, database.md §1); anything else is a malformed invoice, never silently rounded.
    /// </summary>
    private static decimal Amount(XElement element, string currency)
    {
        if ((element.Attribute("currencyID")?.Value ?? currency) != currency)
        {
            throw new UblFormatException($"{element.Name.LocalName} is not in the document currency.");
        }

        var value = element.Value.Trim();
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            || decimal.Round(amount, 2) != amount
            || Math.Abs(decimal.Truncate(amount)) >= 1_000_000_000_000_000m)
        {
            throw new UblFormatException($"{element.Name.LocalName} is not an amount with at most 2 decimals.");
        }

        return amount;
    }
}
