using System.Data;
using Microsoft.Data.SqlClient;

namespace Shared.Infrastructure.Persistence;

/// <summary>SQL Server application locks (<c>sp_getapplock</c>), database.md §4 and ADR-021.</summary>
public static class ApplicationLock
{
    /// <summary>
    /// Takes an exclusive lock owned by <paramref name="transaction"/>; it is released when the transaction ends.
    /// Throws when the lock is not granted within <paramref name="timeout"/>.
    /// </summary>
    public static async Task AcquireForTransactionAsync(SqlTransaction transaction, string resource, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var result = await GetAppLockAsync(transaction.Connection!, transaction, resource, "Transaction", timeout, cancellationToken);
        if (result < 0)
        {
            throw new TimeoutException($"Application lock '{resource}' was not granted (sp_getapplock returned {result}).");
        }
    }

    /// <summary>
    /// Takes an exclusive session lock on a dedicated connection that stays open until the handle is disposed, so only
    /// one instance runs a singleton job. Returns null when another session holds the lock.
    /// </summary>
    public static async Task<IAsyncDisposable?> TryAcquireForSessionAsync(string connectionString, string resource, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            if (await GetAppLockAsync(connection, null, resource, "Session", TimeSpan.Zero, cancellationToken) >= 0)
            {
                return new SessionLock(connection, resource);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private static async Task<int> GetAppLockAsync(
        SqlConnection connection, SqlTransaction? transaction, string resource, string owner, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("sp_getapplock", connection, transaction) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@Resource", resource);
        command.Parameters.AddWithValue("@LockMode", "Exclusive");
        command.Parameters.AddWithValue("@LockOwner", owner);
        command.Parameters.AddWithValue("@LockTimeout", (int)timeout.TotalMilliseconds);
        var returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
        returnValue.Direction = ParameterDirection.ReturnValue;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (int)returnValue.Value;
    }

    private sealed class SessionLock(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new SqlCommand("sp_releaseapplock", connection) { CommandType = CommandType.StoredProcedure };
                command.Parameters.AddWithValue("@Resource", resource);
                command.Parameters.AddWithValue("@LockOwner", "Session");
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                // Closing the session also releases the lock if the explicit release failed.
                await connection.DisposeAsync();
            }
        }
    }
}
