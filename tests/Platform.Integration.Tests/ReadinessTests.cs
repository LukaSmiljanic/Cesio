using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.Integration.Tests.Infrastructure;
using Shared.Infrastructure.Persistence;

namespace Platform.Integration.Tests;

public sealed class ReadinessTests(SqlServerContainer server) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Readiness_is_ok_when_the_database_answers()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting($"ConnectionStrings:{PersistenceSetup.ConnectionStringName}", _database.ConnectionString));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
