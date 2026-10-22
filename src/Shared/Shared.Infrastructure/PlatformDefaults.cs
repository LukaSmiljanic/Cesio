using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared.Infrastructure;

/// <summary>Host defaults shared by Platform.Api and Platform.Worker.</summary>
public static class PlatformDefaults
{
    public static IHostApplicationBuilder AddPlatformDefaults(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton(TimeProvider.System);

        // Console export is for local work only; the Azure Monitor exporter is added with the deployment (task 024).
        var exportToConsole = builder.Environment.IsDevelopment();

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (exportToConsole)
            {
                logging.AddConsoleExporter();
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
                if (exportToConsole)
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter("Cesio.*");
                if (exportToConsole)
                {
                    metrics.AddConsoleExporter();
                }
            });

        return builder;
    }
}
