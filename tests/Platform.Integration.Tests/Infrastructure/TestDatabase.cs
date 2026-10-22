using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Audit;
using Modules.Documents;
using Modules.Identity;
using Modules.Organizations;
using Shared.Infrastructure.Email;
using Shared.Infrastructure.Outbox;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Requests;

namespace Platform.Integration.Tests.Infrastructure;

/// <summary>
/// A fresh, fully migrated database per test class, plus the services the hosts register (persistence, outbox, audit)
/// and the test-only <see cref="SampleDbContext"/>.
/// </summary>
public sealed class TestDatabase(SqlServerContainer server) : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public TestRequestContext RequestContext { get; } = new();

    public ConsumedEvents ConsumedEvents { get; } = new();

    public CapturingEmailSender Emails { get; } = new();

    public ServiceProvider Services { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await server.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        Services = BuildServices(services => services.AddScoped<IRequestContext>(_ => RequestContext));
        await DatabaseMigrator.MigrateAsync(Services, ConnectionString, TestContext.Current.CancellationToken);

        await using var sample = PersistenceSetup.CreateStandalone<SampleDbContext>(ConnectionString, SampleDbContext.SchemaName);
        await sample.Database.ExecuteSqlRawAsync("CREATE SCHEMA sample", TestContext.Current.CancellationToken);
        await sample.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();

    /// <summary>The service registrations of the hosts, on this database; <paramref name="configure"/> adds the request context.</summary>
    public ServiceProvider BuildServices(Action<IServiceCollection> configure, IDictionary<string, string?>? settings = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{PersistenceSetup.ConnectionStringName}"] = ConnectionString })
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        AddServices(services);
        configure(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Everything except configuration and the request context.</summary>
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddPersistence().AddAuditModule().AddIdentityModule().AddOrganizationsModule().AddDocumentsModule();
        // Not through AddModuleDbContext: the sample schema has no migrations; InitializeAsync creates its tables.
        services.AddDbContext<SampleDbContext>((provider, options) => options.UseSqlServer(provider.GetRequiredService<UnitOfWork>().Connection));
        services.AddScoped<SubmitSampleOrder>();
        services.AddSingleton(ConsumedEvents);
        services.AddSingleton<IEmailSender>(Emails);
        services.AddIntegrationEventConsumer<SampleOrderSubmitted, CountingConsumer>("Tests.Counting");
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
