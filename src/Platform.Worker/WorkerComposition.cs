using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

namespace Platform.Worker;

/// <summary>The worker's services, shared with the test that runs every job in this composition.</summary>
public static class WorkerComposition
{
    public static HostApplicationBuilder AddWorkerServices(this HostApplicationBuilder builder)
    {
        // The modules also register HTTP-only services (authorization, the request context of the API) that this host
        // never resolves, so the Development check that every registration can be built is off; scopes are still validated.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));
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
            .AddDocumentsModule()
            .AddAuditJobs();
        return builder;
    }
}
