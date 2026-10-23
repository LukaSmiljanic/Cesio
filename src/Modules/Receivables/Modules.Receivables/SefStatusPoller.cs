using System.Globalization;
using System.Text.Json;
using Integrations.Sef;
using Modules.Organizations.Contracts;
using Modules.Receivables.Persistence;
using Shared.Infrastructure.Secrets;
using Shared.Kernel;

namespace Modules.Receivables;

/// <summary>
/// Re-polls the SEF status of an invoice with the seller's key (tasks 011, 012). The new status and the history row
/// are only tracked; the caller saves them with an <c>Invoice.SefStatusObserved</c> audit event in its transaction.
/// </summary>
internal sealed class SefStatusPoller(
    ReceivablesDbContext receivables,
    ISefClient sef,
    ISefConnections connections,
    ISecretStore secrets,
    TimeProvider timeProvider)
{
    /// <returns>The fresh status, or null when SEF cannot be asked (no working key, unavailable).</returns>
    public async Task<string?> PollAsync(Guid sellerOrgId, Invoice invoice, CancellationToken cancellationToken)
    {
        var connection = await connections.GetAsync(sellerOrgId, cancellationToken);
        var apiKey = connection is { Status: SefConnectionStatus.Connected } ? await secrets.GetAsync(connection.SecretName, cancellationToken) : null;
        if (apiKey is null)
        {
            return null;
        }

        SefSalesInvoice? fresh;
        try
        {
            fresh = await sef.GetSalesInvoiceAsync(apiKey, long.Parse(invoice.SefInvoiceId, CultureInfo.InvariantCulture), cancellationToken);
        }
        catch (SefUnauthorizedException)
        {
            await connections.MarkInvalidAsync(sellerOrgId, cancellationToken);
            return null;
        }
        catch (SefUnavailableException)
        {
            return null;
        }

        var status = fresh?.Status ?? "Deleted";
        var now = timeProvider.GetUtcNow();
        invoice.SefStatus = status;
        invoice.SefStatusCheckedAt = now;
        receivables.SefStatusHistory.Add(new SefStatusObservation
        {
            Id = SequentialGuid.NewGuid(),
            InvoiceId = invoice.Id,
            Status = status,
            ObservedAt = now,
            Raw = JsonSerializer.Serialize(fresh is null ? new { status } : (object)fresh),
        });
        return status;
    }
}
