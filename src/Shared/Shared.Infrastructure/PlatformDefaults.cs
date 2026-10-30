using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
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

        // Links in emails and confirmations are built from it; outside Development a missing value would send localhost links.
        if (!builder.Environment.IsDevelopment()
            && !(Uri.TryCreate(builder.Configuration["Auth:PublicBaseUrl"], UriKind.Absolute, out var publicBase) && publicBase.Scheme == Uri.UriSchemeHttps && publicBase.AbsolutePath == "/"))
        {
            throw new InvalidOperationException("Auth:PublicBaseUrl must be an https:// origin outside Development.");
        }

        builder.Services.AddSingleton(TimeProvider.System);
        AddKeyRing(builder);

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

        // On the server (ADR-023) logs are JSON lines on standard output, collected by Docker.
        if (!exportToConsole)
        {
            builder.Logging.AddJsonConsole();
        }

        return builder;
    }

    /// <summary>
    /// ASP.NET Core data protection (cookies, anti-forgery, confirmation links, stored secrets) with keys shared by the API
    /// and the worker in one directory, so they survive restarts and several instances (ADR-023). Outside Development the
    /// keys are encrypted with a certificate: without it a copy of the directory would reveal every stored secret.
    /// </summary>
    private static void AddKeyRing(IHostApplicationBuilder builder)
    {
        var development = builder.Environment.IsDevelopment();
        var keysPath = builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } configured
            ? configured
            : development
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cesio", "dev", "keys")
                : throw new InvalidOperationException("DataProtection:KeysPath must be set outside Development.");
        var keyRing = builder.Services.AddDataProtection().SetApplicationName("Cesio").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (builder.Configuration["DataProtection:CertificatePath"] is { Length: > 0 } certificatePath)
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["DataProtection:CertificatePassword"]);
            keyRing.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        }
        else if (!development)
        {
            throw new InvalidOperationException("DataProtection:CertificatePath must be set outside Development: the keys protect the stored secrets.");
        }
    }
}
