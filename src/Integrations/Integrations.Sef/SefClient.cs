using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Integrations.Sef;

/// <summary>
/// Read-only SEF public API (integrations.md §1.1, DEMO Swagger <c>public_v1</c>). The adapter has no write methods:
/// it never sends, cancels, stornos or deletes invoices, although the same API key would allow it (LQ-51).
/// </summary>
public interface ISefClient
{
    /// <summary>Cheap call that proves the key works (<c>GET /getEfakturaVersion</c>).</summary>
    Task CheckKeyAsync(string apiKey, CancellationToken cancellationToken);

    /// <summary>Sales invoice IDs by date (<c>POST /sales-invoice/ids</c>; a query despite the verb).</summary>
    Task<IReadOnlyList<long>> ListSalesInvoiceIdsAsync(string apiKey, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The invoice's SEF status (<c>GET /sales-invoice</c>); null when SEF does not know the ID.</summary>
    Task<SefSalesInvoice?> GetSalesInvoiceAsync(string apiKey, long invoiceId, CancellationToken cancellationToken);

    /// <summary>The UBL document (<c>GET /sales-invoice/xml</c>).</summary>
    Task<byte[]> GetSalesInvoiceUblAsync(string apiKey, long invoiceId, CancellationToken cancellationToken);
}

/// <summary>The parts of <c>SimpleSalesInvoiceDto</c> the platform uses; amounts and parties come from the UBL.</summary>
public sealed record SefSalesInvoice(long InvoiceId, string Status, DateTimeOffset? LastModifiedUtc);

/// <summary>SEF answered 401: the key is wrong, revoked or for another environment.</summary>
public sealed class SefUnauthorizedException() : Exception("SEF rejected the API key.");

/// <summary>SEF is unreachable or kept failing after the retries (fail closed, integrations.md §1).</summary>
public sealed class SefUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed partial class HttpSefClient(HttpClient http, ILogger<HttpSefClient> logger) : ISefClient
{
    private const string ApiKeyHeader = "ApiKey";

    public async Task CheckKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/publicApi/getEfakturaVersion", apiKey, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<long>> ListSalesInvoiceIdsAsync(string apiKey, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture,
            $"api/publicApi/sales-invoice/ids?dateFrom={from:yyyy-MM-dd}T00:00:00&dateTo={to:yyyy-MM-dd}T23:59:59");
        using var response = await SendAsync(HttpMethod.Post, path, apiKey, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SalesInvoicesDto>(cancellationToken)
            ?? throw new SefUnavailableException("SEF returned an empty invoice list.");
        return body.SalesInvoiceIds ?? [];
    }

    public async Task<SefSalesInvoice?> GetSalesInvoiceAsync(string apiKey, long invoiceId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/publicApi/sales-invoice?invoiceId={invoiceId}", apiKey, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<SimpleSalesInvoiceDto>(cancellationToken)
            ?? throw new SefUnavailableException("SEF returned an empty invoice.");
        return new SefSalesInvoice(dto.InvoiceId, dto.Status ?? "Unknown", dto.LastModifiedUtc);
    }

    public async Task<byte[]> GetSalesInvoiceUblAsync(string apiKey, long invoiceId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"api/publicApi/sales-invoice/xml?invoiceId={invoiceId}", apiKey, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(ApiKeyHeader, apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new SefUnavailableException("SEF is not reachable.", exception);
        }

        // Path only: the key is a header and never logged; invoice IDs are not personal data.
        var endpoint = path.Split('?')[0];
        var statusCode = (int)response.StatusCode;
        LogCall(logger, method.Method, endpoint, statusCode);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new SefUnauthorizedException();
        }

        if ((int)response.StatusCode >= 500)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new SefUnavailableException($"SEF answered {status}.");
        }

        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SEF {Method} {Path} → {Status}")]
    private static partial void LogCall(ILogger logger, string method, string path, int status);

    private sealed record SalesInvoicesDto([property: JsonPropertyName("salesInvoiceIds")] long[]? SalesInvoiceIds);

    private sealed record SimpleSalesInvoiceDto(
        [property: JsonPropertyName("invoiceId")] long InvoiceId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("lastModifiedUtc")] DateTimeOffset? LastModifiedUtc);
}

/// <summary>
/// Retries transient failures (5xx, network errors, per-attempt timeout of 10 s) up to 3 times with exponential
/// backoff and jitter; 4xx is never retried (integrations.md §1).
/// </summary>
public sealed class SefRetryHandler : DelegatingHandler
{
    public const int MaxRetries = 3;

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _baseDelay;

    public SefRetryHandler()
        : this(TimeSpan.FromMilliseconds(200))
    {
    }

    public SefRetryHandler(TimeSpan baseDelay) => _baseDelay = baseDelay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(AttemptTimeout);
            try
            {
                var response = await base.SendAsync(request, attemptTimeout.Token);
                if ((int)response.StatusCode < 500 || attempt == MaxRetries)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (Exception exception) when (attempt < MaxRetries && !cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or OperationCanceledException)
            {
                // Transient: retried below.
            }

            var delay = (_baseDelay * Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)_baseDelay.TotalMilliseconds + 1));
            await Task.Delay(delay, cancellationToken);
        }
    }
}
