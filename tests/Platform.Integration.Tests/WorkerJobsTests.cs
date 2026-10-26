using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Integration.Tests.Infrastructure;
using Platform.Worker;
using Shared.Infrastructure.Jobs;

namespace Platform.Integration.Tests;

/// <summary>
/// Every scheduled job runs in the worker's own composition (not only inside the API test host), so a service the worker
/// does not register fails here instead of in every run after a deploy.
/// </summary>
public sealed class WorkerJobsTests(SqlServerContainer server, AzuriteContainer azurite) : IAsyncLifetime
{
    private readonly TestDatabase _database = new(server);

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Every_job_runs_in_the_worker_composition()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cesio"] = _database.ConnectionString,
            ["ConnectionStrings:Documents"] = azurite.ConnectionString,
            ["ConnectionStrings:AuditDigests"] = azurite.ConnectionString,
            ["Sef:Client"] = "Fake",
        });
        builder.AddWorkerServices();
        // The scheduler and the outbox dispatcher stay off; the test runs each job once.
        builder.Services.RemoveAll<IHostedService>();
        using var host = builder.Build();
        var runner = host.Services.GetRequiredService<JobRunner>();

        var failed = new List<string>();
        foreach (var definition in runner.Definitions)
        {
            var run = await runner.RunAsync(definition.Name, TestContext.Current.CancellationToken);
            if (run.Status != "Succeeded")
            {
                failed.Add($"{run.Name}: {run.Status} {run.Error}");
            }
        }

        Assert.True(runner.Definitions.Count > 15, $"Only {runner.Definitions.Count} jobs found.");
        Assert.True(failed.Count == 0, "Jobs that fail in the worker:\n" + string.Join("\n", failed));
    }
}
