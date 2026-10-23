using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Audit;
using Modules.Documents;
using Modules.Identity;
using Modules.Organizations;
using Modules.Receivables;
using Modules.Deals;
using Modules.Fees;
using Modules.Marketplace;
using Shared.Infrastructure.Persistence;

// Applies all module migrations and the database roles (task 003 item 8). CI/CD runs it with the migrator identity
// before the new version starts: dotnet run --project src/Platform.Migrator
var builder = Host.CreateApplicationBuilder(args);
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
    .AddFeesModule()
    .AddDealsModule();

using var host = builder.Build();
await DatabaseMigrator.MigrateAsync(host.Services, PersistenceSetup.ConnectionString(builder.Configuration), CancellationToken.None);
