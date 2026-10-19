using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Modules.Audit;
using Modules.Compliance;
using Modules.Deals;
using Modules.Fees;
using Modules.Identity;
using Modules.Marketplace;
using Modules.Notifications;
using Modules.Organizations;
using Modules.Receivables;
using Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformDefaults("platform-api");
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services
    .AddIdentityModule()
    .AddOrganizationsModule()
    .AddComplianceModule()
    .AddReceivablesModule()
    .AddMarketplaceModule()
    .AddDealsModule()
    .AddFeesModule()
    .AddNotificationsModule()
    .AddAuditModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Liveness has no checks; readiness gets the database check in task 003.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
