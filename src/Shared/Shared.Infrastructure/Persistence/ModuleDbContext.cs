using Microsoft.EntityFrameworkCore;
using Shared.Kernel;

namespace Shared.Infrastructure.Persistence;

/// <summary>
/// Base for every module's DbContext: its own schema, the database.md §1 conventions (snake_case, decimal(19,4),
/// datetimeoffset(3), currency as char(3), typed ID columns, <c>version</c> concurrency token) and the shared
/// connection and transaction of the <see cref="UnitOfWork"/>.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private const string VersionProperty = "Version";

    protected ModuleDbContext(DbContextOptions options, UnitOfWork? unitOfWork)
        : base(options) => unitOfWork?.Enlist(this);

    protected abstract string Schema { get; }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Modified && entry.Metadata.FindProperty(VersionProperty) is { IsConcurrencyToken: true })
            {
                var version = entry.Property(VersionProperty);
                version.CurrentValue = (int)version.OriginalValue! + 1;
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
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

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

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
}
