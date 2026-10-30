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

// The API and worker sign in with a SQL login in app_rw (ADR-023); its password comes from the environment
// (Database__AppLogin__Password), never from a settings file.
if (builder.Configuration["Database:AppLogin:Name"] is { Length: > 0 } login)
{
    await DatabaseMigrator.AddApplicationLoginAsync(
        PersistenceSetup.ConnectionString(builder.Configuration),
        login,
        builder.Configuration["Database:AppLogin:Password"] ?? throw new InvalidOperationException("Database:AppLogin:Password must be set with Database:AppLogin:Name."),
        CancellationToken.None);
}
