using Microsoft.EntityFrameworkCore;
using Modules.Audit.Contracts;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Kernel;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>A test-only module (task 003 acceptance: a sample command shows state change + audit + outbox in one transaction).</summary>
public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options, UnitOfWork? unitOfWork)
    : ModuleDbContext(options, unitOfWork)
{
    public const string SchemaName = "sample";

    public DbSet<SampleOrder> Orders => Set<SampleOrder>();

    protected override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SampleOrder>(order =>
        {
            order.ToTable("orders");
            order.HasKey(o => o.Id);
            order.Property(o => o.State).HasMaxLength(40);
        });
}

public sealed class SampleOrder
{
    public Guid Id { get; init; }

    public string State { get; set; } = "Draft";

    public int Version { get; set; }
}

public sealed record SampleOrderSubmitted(Guid OrderId) : IIntegrationEvent
{
    public static string EventType => "Tests.SampleOrderSubmitted";
}

/// <summary>Counts deliveries per event, so tests can assert exactly-once side effects.</summary>
public sealed class ConsumedEvents
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public Func<Exception?> Failure { get; set; } = () => null;

    public void Record() => Interlocked.Increment(ref _count);
}

public sealed class CountingConsumer(ConsumedEvents consumed) : IIntegrationEventConsumer<SampleOrderSubmitted>
{
    public Task HandleAsync(SampleOrderSubmitted integrationEvent, Guid eventId, CancellationToken cancellationToken)
    {
        if (consumed.Failure() is { } failure)
        {
            throw failure;
        }

        consumed.Record();
        return Task.CompletedTask;
    }
}

/// <summary>The sample command: Draft → Submitted, with its audit event and integration event in the same transaction.</summary>
public sealed class SubmitSampleOrder(UnitOfWork unitOfWork, SampleDbContext sample, IAuditWriter audit, IOutbox outbox)
{
    public Task ExecuteAsync(Guid orderId, Action? afterWrites, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await sample.Orders.SingleAsync(o => o.Id == orderId, ct);
                var previous = order.State;
                order.State = "Submitted";
                await sample.SaveChangesAsync(ct);
                await audit.WriteAsync(new AuditEntry("SampleOrder.Submitted", "SampleOrder", order.Id, PreviousState: previous, NewState: order.State), ct);
                await outbox.AddAsync(new SampleOrderSubmitted(order.Id), ct);
                afterWrites?.Invoke();
            },
            cancellationToken);
}
