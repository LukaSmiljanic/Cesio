using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shared.Infrastructure.Persistence;

namespace Modules.Identity.Persistence;

/// <summary>
/// The <c>auth</c> schema (<c>identity</c> is a T-SQL reserved word): ASP.NET Core Identity users, claims, logins and
/// tokens, plus sessions and hashed recovery codes. It derives from <see cref="IdentityUserContext{TUser, TKey}"/>, so it
/// applies the <see cref="ModuleConventions"/> itself instead of deriving from <see cref="ModuleDbContext"/>.
/// </summary>
public sealed class AuthDbContext : IdentityUserContext<AuthUser, Guid>
{
    public const string SchemaName = "auth";

    public AuthDbContext(DbContextOptions<AuthDbContext> options, UnitOfWork? unitOfWork)
        : base(options) => unitOfWork?.Enlist(this);

    internal DbSet<AuthSession> Sessions => Set<AuthSession>();

    internal DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ModuleConventions.IncrementVersions(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModuleConventions.Configure(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(builder);

        builder.Entity<AuthUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<AuthSession>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.IpAddress).HasMaxLength(45).IsUnicode(false);
            session.Property(s => s.UserAgent).HasMaxLength(512);
            session.HasOne<AuthUser>().WithMany().HasForeignKey(s => s.UserId);
            session.HasIndex(s => new { s.UserId, s.RevokedAt });
        });

        builder.Entity<RecoveryCode>(code =>
        {
            code.ToTable("recovery_codes");
            code.HasKey(c => c.Id);
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.CodeHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            code.HasOne<AuthUser>().WithMany().HasForeignKey(c => c.UserId);
            code.HasIndex(c => c.UserId);
        });

        ModuleConventions.Finish(builder);
    }
}

internal sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        PersistenceSetup.CreateForDesignTime<AuthDbContext>(AuthDbContext.SchemaName);
}
