using Modules.Compliance.Contracts;
using Shared.Infrastructure.Outbox;

namespace Modules.Marketplace;

/// <summary>
/// INV-A2 consequences of a revoked or expired factor authorization: the factor's Active offers expire and its deals
/// before Signed get a hold. Until then the factor is already blocked, because every offer and acceptance asks
/// <see cref="IAuthorizationQuery"/>.
/// </summary>
internal sealed class FactorAuthorizationEndedConsumer : IIntegrationEventConsumer<FactorAuthorizationRevoked>, IIntegrationEventConsumer<FactorAuthorizationExpired>
{
    public const string RevokedConsumerName = "marketplace.factor-authorization-revoked";

    public const string ExpiredConsumerName = "marketplace.factor-authorization-expired";

    // TODO(task 020): expire the factor's Active offers and place holds on its deals before Signed.
    public Task HandleAsync(FactorAuthorizationRevoked integrationEvent, Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;

    // TODO(task 020): the same consequences as a revocation.
    public Task HandleAsync(FactorAuthorizationExpired integrationEvent, Guid eventId, CancellationToken cancellationToken) => Task.CompletedTask;
}
