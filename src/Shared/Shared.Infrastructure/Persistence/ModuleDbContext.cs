using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Shared.Kernel;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// Base for every module's DbContext: its own schema, the database.md §1 conventions (snake_case, decimal(19,4),
/// datetimeoffset(3), currency as char(3), typed ID columns, <c>version</c> concurrency token) and the shared
/// connection and transaction of the <see cref="UnitOfWork"/>.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    protected ModuleDbContext(DbContextOptions options, UnitOfWork? unitOfWork)
        : base(options) => unitOfWork?.Enlist(this);

    protected abstract string Schema { get; }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ModuleConventions.IncrementVersions(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModuleConventions.Configure(configurationBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);
        ModuleConventions.Finish(modelBuilder);
    }
}

/// <summary>
/// The conventions of <see cref="ModuleDbContext"/>, for contexts that must derive from another base class
/// (the Identity context derives from <c>IdentityUserContext</c>).
/// </summary>
public static class ModuleConventions
{
    private const string VersionProperty = "Version";

    public static void Configure(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<DateTimeOffset>().HavePrecision(3);
        configurationBuilder.Properties<Currency>().HaveConversion<CurrencyConverter>().HaveMaxLength(3).AreFixedLength().AreUnicode(false);
        foreach (var idType in TypedIdConverter.IdTypes)
        {
            configurationBuilder.Properties(idType).HaveConversion(typeof(TypedIdConverter<>).MakeGenericType(idType));
        }
    }

    /// <summary>Marks <c>Version</c> properties as concurrency tokens and renames everything to snake_case. Call last.</summary>
    public static void Finish(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var version = entityType.FindProperty(VersionProperty);
            if (version is not null && version.ClrType == typeof(int) && !version.IsKey())
            {
                version.IsConcurrencyToken = true;
            }
        }

        SnakeCaseNames.Apply(modelBuilder.Model);
    }

    /// <summary>Increments the <c>version</c> token of every modified entity before saving.</summary>
    public static void IncrementVersions(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(VersionProperty) is { IsConcurrencyToken: true })
            {
                var version = entry.Property(VersionProperty);
                version.CurrentValue = (int)version.OriginalValue! + 1;
            }
        }
    }
}
