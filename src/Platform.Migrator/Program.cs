using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Audit;
using Modules.Documents;
using Modules.Identity;
using Modules.Organizations;
using Modules.Receivables;
using Modules.Compliance;
using Modules.Deals;
using Modules.Fees;
using Modules.Marketplace;
using Modules.Notifications;
using Shared.Infrastructure.Persistence;

// Applies all module migrations and the database roles (task 003 item 8). CI/CD runs it with the migrator identity
// before the new version starts: dotnet run --project src/Platform.Migrator
var builder = Host.CreateApplicationBuilder(args);
// The modules also register HTTP-only services (authorization, the request context of the API) that this host never
// resolves, so the Development check that every registration can be built is off; scopes are still validated.
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false }));
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddPersistence()
    .AddAuditModule()
    .AddDocumentsModule()
    .AddIdentityModule()
    .AddOrganizationsModule()
    .AddReceivablesModule()
    .AddMarketplaceModule()
    .AddComplianceModule()
    .AddFeesModule()
    .AddDealsModule()
    .AddNotificationsModule();

using var host = builder.Build();
await DatabaseMigrator.MigrateAsync(host.Services, PersistenceSetup.ConnectionString(builder.Configuration), CancellationToken.None);

// In Azure SQL the API and worker sign in with their managed identity (task 024). Each one configured here becomes a
// database user of type E (from its client ID, no directory lookup needed) and a member of app_rw, nothing more.
foreach (var principal in builder.Configuration.GetSection("Database:AppPrincipals").GetChildren())
{
    await DatabaseMigrator.AddApplicationPrincipalAsync(
        PersistenceSetup.ConnectionString(builder.Configuration), principal["Name"]!, Guid.Parse(principal["ClientId"]!), CancellationToken.None);
}
