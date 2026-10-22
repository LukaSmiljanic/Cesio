using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shared.Kernel;

namespace Shared.Infrastructure.Persistence;

/// <summary>Stores a <see cref="Currency"/> as its ISO code in a char(3) column.</summary>
public sealed class CurrencyConverter() : ValueConverter<Currency, string>(currency => currency.Code, code => Currency.FromCode(code));

/// <summary>Stores a typed ID as its <see cref="Guid"/>.</summary>
public sealed class TypedIdConverter<T>() : ValueConverter<T, Guid>(id => id.Value, value => FromGuid(value))
    where T : struct, ITypedId<T>
{
    // Expression trees cannot call static abstract interface members directly.
    private static T FromGuid(Guid value) => T.From(value);
}

internal static class TypedIdConverter
{
    /// <summary>Every typed ID in Shared.Kernel; each gets a column converter by convention.</summary>
    public static readonly Type[] IdTypes = typeof(ITypedId<>).Assembly.GetExportedTypes()
        .Where(type => type.IsValueType && type.GetInterfaces().Any(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ITypedId<>) && i.GenericTypeArguments[0] == type))
        .ToArray();
}

public static class MappingExtensions
{
    /// <summary>
    /// Maps a <see cref="Money"/> property to an amount column (decimal(19,4)) and a currency column (char(3)) with the
    /// allow-list CHECK constraint from database.md §1.
    /// </summary>
    public static ComplexPropertyBuilder<Money> HasMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, Money?>> property,
        string amountColumn,
        string currencyColumn)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        var money = builder.ComplexProperty(property);
        money.Property(m => m.Amount).HasColumnName(amountColumn);
        money.Property(m => m.Currency).HasColumnName(currencyColumn);
        builder.ToTable(table => table.HasCheckConstraint(
            $"ck_{SnakeCaseNames.Convert(typeof(TEntity).Name)}_{currencyColumn}",
            $"[{currencyColumn}] IN ('RSD','EUR')"));
        return money;
    }

    /// <summary>Adds <c>CHECK (ISJSON(column) = 1)</c> (database.md §1); a nullable column may also be NULL.</summary>
    public static EntityTypeBuilder<TEntity> HasJsonCheck<TEntity>(this EntityTypeBuilder<TEntity> builder, string column, bool nullable = false)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        var predicate = nullable ? $"[{column}] IS NULL OR ISJSON([{column}]) = 1" : $"ISJSON([{column}]) = 1";
        builder.ToTable(table => table.HasCheckConstraint($"ck_{SnakeCaseNames.Convert(typeof(TEntity).Name)}_{column}_json", predicate));
        return builder;
    }
}
