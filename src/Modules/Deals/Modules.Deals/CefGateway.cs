using System.Globalization;
using Shared.Infrastructure.Persistence.Platform;

namespace Modules.Deals;

/// <summary>The Central Register of Factoring Agreements (CEF, integrations.md §2); the API integration is out of scope.</summary>
internal interface ICefGateway
{
    Task<bool> IsOperationalAsync(DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>
/// The CEF is not live yet (ADR-019): it counts as operational only from the business parameter
/// <c>cef.operationalFrom</c> (an ISO date-time), which is unset until it is.
/// </summary>
internal sealed class CefNotOperationalGateway(IBusinessParameters parameters) : ICefGateway
{
    public const string OperationalFrom = "cef.operationalFrom";

    public async Task<bool> IsOperationalAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        DateTimeOffset.TryParse(await parameters.GetAsync(OperationalFrom, string.Empty, cancellationToken), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from)
        && from <= at;
}
