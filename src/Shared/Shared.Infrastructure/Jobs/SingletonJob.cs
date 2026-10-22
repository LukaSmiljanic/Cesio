using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Persistence;

namespace Shared.Infrastructure.Jobs;

/// <summary>
/// A periodic background job that runs on one instance at a time: each run holds an application lock with owner
/// <c>Session</c> on a dedicated connection (database.md §4). A failed run is logged and retried at the next interval.
/// </summary>
public abstract class SingletonJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger logger) : BackgroundService
{
    protected abstract string LockName { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>One run inside its own DI scope.</summary>
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await TryRunAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TryRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var jobLock = await ApplicationLock.TryAcquireForSessionAsync(
                PersistenceSetup.ConnectionString(configuration), $"job:{LockName}", cancellationToken);
            if (jobLock is null)
            {
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            await RunOnceAsync(scope.ServiceProvider, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            JobLog.RunFailed(logger, exception, LockName);
        }
    }
}

internal static partial class JobLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed; it runs again at the next interval.")]
    public static partial void RunFailed(ILogger logger, Exception exception, string job);
}
