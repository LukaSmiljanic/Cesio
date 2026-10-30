using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// Applies every registered module's migrations in order, then the database roles and grants (database.md §1, task 003
/// items 6 and 8). Used by Platform.Migrator in CI/CD and by the integration tests.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>Schemas whose tables are append-only for the application role (audit.md §1).</summary>
    public static readonly IReadOnlySet<string> AppendOnlySchemas = new HashSet<string>(StringComparer.Ordinal) { "audit" };

    public static async Task MigrateAsync(IServiceProvider services, string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var migrators = services.GetServices<IModuleMigrator>().OrderBy(m => m.Order).ToList();
        foreach (var migrator in migrators)
        {
            await migrator.MigrateAsync(connectionString, cancellationToken);
        }

        await ApplyRolesAsync(connectionString, migrators.Select(m => m.Schema), cancellationToken);
    }

    /// <summary>
    /// The SQL login the API and worker sign in with (ADR-023): created, or its password rotated, and made a database user
    /// in <c>app_rw</c>, nothing more. Needs a server administrator (the migrator's own login). Idempotent. The name and
    /// password are passed as parameters and quoted in SQL, never concatenated as given.
    /// </summary>
    public static async Task AddApplicationLoginAsync(string connectionString, string login, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        const string sql = """
            DECLARE @statement nvarchar(max) = CASE WHEN SUSER_ID(@login) IS NULL THEN N'CREATE LOGIN ' ELSE N'ALTER LOGIN ' END
                + QUOTENAME(@login) + N' WITH PASSWORD = N''' + REPLACE(@password, N'''', N'''''') + N'''';
            EXEC (@statement);
            IF DATABASE_PRINCIPAL_ID(@login) IS NULL
            BEGIN
                SET @statement = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
                EXEC (@statement);
            END;
            SET @statement = N'ALTER ROLE app_rw ADD MEMBER ' + QUOTENAME(@login);
            EXEC (@statement);
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@login", login);
        command.Parameters.AddWithValue("@password", password);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Creates <c>app_rw</c>, <c>migrator</c> and <c>readonly_reporting</c> if missing and grants them per schema.
    /// <c>app_rw</c> gets INSERT and SELECT only on append-only schemas, with UPDATE and DELETE denied; TRUNCATE needs
    /// ALTER, which no application role has. Idempotent. Needs a principal that may manage roles (database.md §6).
    /// </summary>
    private static async Task ApplyRolesAsync(string connectionString, IEnumerable<string> schemas, CancellationToken cancellationToken)
    {
        var statements = new List<string>
        {
            "IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw;",
            "IF DATABASE_PRINCIPAL_ID('migrator') IS NULL CREATE ROLE migrator;",
            "IF DATABASE_PRINCIPAL_ID('readonly_reporting') IS NULL CREATE ROLE readonly_reporting;",
            "ALTER ROLE db_ddladmin ADD MEMBER migrator;",
            "ALTER ROLE db_datareader ADD MEMBER migrator;",
            "ALTER ROLE db_datawriter ADD MEMBER migrator;",
        };
        foreach (var schema in schemas.Distinct(StringComparer.Ordinal))
        {
            var quoted = $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}]";
            statements.Add(AppendOnlySchemas.Contains(schema)
                ? $"GRANT SELECT, INSERT ON SCHEMA::{quoted} TO app_rw; DENY UPDATE, DELETE ON SCHEMA::{quoted} TO app_rw;"
                : $"GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::{quoted} TO app_rw;");
        }

        // readonly_reporting gets no grants until task 022 defines the reporting scope (database.md §6: never personal
        // data in compliance/orgs). Stored command responses are a replay cache, never reporting data.
        statements.Add("IF OBJECT_ID('platform.idempotency_keys') IS NOT NULL DENY SELECT ON platform.idempotency_keys TO readonly_reporting;");
        // Encrypted secrets (ADR-023) are never reporting data either.
        statements.Add("IF OBJECT_ID('platform.secrets') IS NOT NULL DENY SELECT ON platform.secrets TO readonly_reporting;");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        foreach (var statement in statements)
        {
            await using var command = new SqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
