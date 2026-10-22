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

var builder = Host.CreateApplicationBuilder(args);

builder.AddPlatformDefaults("platform-worker");
builder.Services.AddWorkerInfrastructure();

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
    .AddAuditJobs();

builder.Build().Run();
