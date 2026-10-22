using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// One <see cref="SqlConnection"/> and at most one transaction per scope (request or job run). Every module
/// <see cref="ModuleDbContext"/> in the scope uses this connection and joins the transaction, so a state change,
/// its audit event and its outbox message commit or roll back together (ADR-006, audit.md §1).
/// </summary>
public sealed class UnitOfWork : IAsyncDisposable
{
    private readonly List<DbContext> _contexts = [];

    public UnitOfWork(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Connection = new SqlConnection(PersistenceSetup.ConnectionString(configuration));
    }

    public SqlConnection Connection { get; }

    public SqlTransaction? Transaction { get; private set; }

    /// <summary>The active transaction; throws when none is active.</summary>
    public SqlTransaction RequiredTransaction =>
        Transaction ?? throw new InvalidOperationException("This operation must run inside the unit of work's transaction.");

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active in this unit of work.");
        }

        await OpenAsync(cancellationToken);
        Transaction = (SqlTransaction)await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(Transaction, cancellationToken);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await RequiredTransaction.CommitAsync(cancellationToken);
        await EndAsync();
    }

    public async Task RollbackAsync()
    {
        if (Transaction is null)
        {
            return;
        }

        await Transaction.RollbackAsync();
        await EndAsync();
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a new transaction: commit on success, rollback on any exception. Inside an
    /// active transaction (an idempotent endpoint already opened one) the work joins it, and the outer owner commits.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Transaction is not null)
        {
            return await work(cancellationToken);
        }

        await BeginAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await RollbackAsync();
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await ExecuteInTransactionAsync(
            async ct =>
            {
                await work(ct);
                return true;
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync();
        await Connection.DisposeAsync();
    }

    /// <summary>Makes <paramref name="context"/> use this scope's transaction; module contexts call it from their constructor.</summary>
    public void Enlist(DbContext context)
    {
        _contexts.Add(context);
        if (Transaction is not null)
        {
            context.Database.UseTransaction(Transaction);
        }
    }

    private async Task EndAsync()
    {
        foreach (var context in _contexts)
        {
            await context.Database.UseTransactionAsync(null);
        }

        await Transaction!.DisposeAsync();
        Transaction = null;
    }
}
