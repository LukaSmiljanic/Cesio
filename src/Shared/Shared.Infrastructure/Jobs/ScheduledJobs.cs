using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Persistence;
using Shared.Infrastructure.Persistence.Platform;
using Shared.Kernel;

namespace Shared.Infrastructure.Jobs;

/// <summary>
/// A scheduled job (task 020): a name, an interval and the work, which returns how many items it processed. Modules
/// register their jobs in every host, so the API can list and trigger them; only the worker schedules them.
/// </summary>
public sealed record JobDefinition(string Name, TimeSpan Interval, Func<IServiceProvider, CancellationToken, Task<int>> Run);

public static class JobSetup
{
    public static IServiceCollection AddJob(this IServiceCollection services, string name, TimeSpan interval, Func<IServiceProvider, CancellationToken, Task<int>> run)
    {
        services.AddSingleton(new JobDefinition(name, interval, run));
        services.TryAddJobRunner();
        return services;
    }

    /// <summary>The worker runs every registered job on its interval.</summary>
    public static IServiceCollection AddJobScheduler(this IServiceCollection services)
    {
        services.TryAddJobRunner();
        services.AddHostedService<JobScheduler>();
        return services;
    }

    private static void TryAddJobRunner(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(JobRunner)))
        {
            services.AddSingleton<JobRunner>();
        }
    }
}

/// <summary>A run's outcome, for <c>GET /admin/jobs</c>.</summary>
public sealed record JobRunInfo(string Name, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status, int Items, string? Error);

/// <summary>
/// Runs a job once under its application lock (database.md §4, one run per cluster) in its own scope, and records the
/// run in <c>platform.job_runs</c> (last run, items processed, failures).
/// </summary>
public sealed partial class JobRunner(IEnumerable<JobDefinition> definitions, IServiceScopeFactory scopeFactory, IConfiguration configuration, TimeProvider timeProvider, ILogger<JobRunner> logger)
{
    public IReadOnlyList<JobDefinition> Definitions { get; } = definitions.ToList();

    /// <summary>Runs the job; <c>Skipped</c> when another instance holds its lock.</summary>
    /// <param name="triggeredBy">The staff user of a manual run (task 020); null for the schedule.</param>
    public async Task<JobRunInfo> RunAsync(string name, CancellationToken cancellationToken, Guid? triggeredBy = null)
    {
        var definition = Definitions.SingleOrDefault(d => d.Name == name) ?? throw new ArgumentException($"Unknown job {name}.", nameof(name));
        var startedAt = timeProvider.GetUtcNow();
        await using var jobLock = await ApplicationLock.TryAcquireForSessionAsync(PersistenceSetup.ConnectionString(configuration), $"job:{name}", cancellationToken);
        if (jobLock is null)
        {
            return new JobRunInfo(name, startedAt, startedAt, "Skipped", 0, null);
        }

        var status = "Succeeded";
        var items = 0;
        string? error = null;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            try
            {
                items = await definition.Run(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status = "Failed";
                error = exception.GetType().Name;
                RunFailed(logger, exception, name);
            }
        }

        var run = new JobRun { Id = SequentialGuid.NewGuid(), Name = name, StartedAt = startedAt, FinishedAt = timeProvider.GetUtcNow(), Status = status, Items = items, Error = error, TriggeredBy = triggeredBy };
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            platform.JobRuns.Add(run);
            await platform.SaveChangesAsync(cancellationToken);
        }

        return new JobRunInfo(name, run.StartedAt, run.FinishedAt, status, items, error);
    }

    /// <summary>Every job with its interval and its last run.</summary>
    public async Task<IReadOnlyList<object>> StatusAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var names = Definitions.Select(d => d.Name).ToList();
        var last = await platform.JobRuns.AsNoTracking()
            .Where(r => names.Contains(r.Name))
            .GroupBy(r => r.Name)
            .Select(g => g.OrderByDescending(r => r.StartedAt).First())
            .ToListAsync(cancellationToken);
        var failures = await platform.JobRuns.AsNoTracking()
            .Where(r => names.Contains(r.Name) && r.Status == "Failed" && r.StartedAt > timeProvider.GetUtcNow().AddDays(-1))
            .GroupBy(r => r.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return Definitions.Select(d =>
        {
            var run = last.SingleOrDefault(r => r.Name == d.Name);
            return (object)new
            {
                name = d.Name,
                intervalMinutes = (int)d.Interval.TotalMinutes,
                lastStartedAt = run?.StartedAt,
                lastStatus = run?.Status,
                lastItems = run?.Items,
                lastError = run?.Error,
                failuresLast24h = failures.SingleOrDefault(f => f.Name == d.Name)?.Count ?? 0,
            };
        }).ToList();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed; it runs again at the next interval.")]
    private static partial void RunFailed(ILogger logger, Exception exception, string job);
}

/// <summary>The worker's loop: every job on its own timer; a failed run is recorded and retried at the next tick.</summary>
internal sealed partial class JobScheduler(JobRunner runner, ILogger<JobScheduler> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(runner.Definitions.Select(definition => LoopAsync(definition, stoppingToken)));

    private async Task LoopAsync(JobDefinition definition, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(definition.Interval);
        do
        {
            try
            {
                await runner.RunAsync(definition.Name, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The lock or the run record could not be written (e.g. the database is down): one job must not stop
                // the others or the host; the next tick tries again. The exception type only (no personal data).
                LoopFailed(logger, definition.Name, exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} could not run or record its run ({Error}); it runs again at the next interval.")]
    private static partial void LoopFailed(ILogger logger, string job, string error);
}
