namespace Shared.Kernel;

/// <summary>
/// An event published to other modules through the outbox (architecture.md §3, rule 3). The type name is stored with
/// the message, so it must stay stable when the class is renamed.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Stable name, e.g. <c>Deals.DealFunded</c>.</summary>
    static abstract string EventType { get; }
}
