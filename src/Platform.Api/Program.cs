using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Modules.Audit;
using Modules.Compliance;
using Modules.Deals;
using Modules.Documents;
using Modules.Fees;
using Modules.Identity;
using Modules.Marketplace;
using Modules.Notifications;
using Modules.Organizations;
using Modules.Receivables;
using Shared.Infrastructure;
using Shared.Infrastructure.Idempotency;
using Shared.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformDefaults("platform-api");
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.AddKernelConverters();
    // Enums are API strings ("Draft", "Seller"), never numbers.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddApiInfrastructure();

builder.Services
    .AddIdentityModule()
    .AddOrganizationsModule()
    .AddComplianceModule()
    .AddReceivablesModule()
    .AddMarketplaceModule()
    .AddDealsModule()
    .AddFeesModule()
    .AddNotificationsModule()
    .AddAuditModule()
    .AddDocumentsModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseIdentityModule();
app.UseOrganizationContext();
app.UseIdempotency();

// Liveness has no checks; readiness includes the database.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(HostSetup.ReadyTag) });

app.MapIdentityEndpoints(includeDevelopmentEndpoints: app.Environment.IsDevelopment());
app.MapOrganizationEndpoints(includeDevelopmentEndpoints: app.Environment.IsDevelopment());
app.MapDocumentEndpoints();
app.MapReceivableEndpoints();
app.MapMarketplaceEndpoints();

app.Run();

public partial class Program;
