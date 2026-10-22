using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Shared.Infrastructure.Persistence;

/// <summary>Renames tables, columns, keys, foreign keys and indexes to snake_case (database.md §1).</summary>
public static class SnakeCaseNames
{
    public static string Convert(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previous = i > 0 ? name[i - 1] : '_';
                var next = i + 1 < name.Length ? name[i + 1] : '_';
                if (previous != '_' && (char.IsLower(previous) || char.IsDigit(previous) || char.IsLower(next)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    internal static void Apply(IMutableModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is { } table)
            {
                entityType.SetTableName(Convert(table));
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(Convert(property.GetColumnName()));
            }

            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties())
                {
                    property.SetColumnName(Convert(property.GetColumnName()));
                }
            }

            foreach (var key in entityType.GetKeys())
            {
                key.SetName(Convert(key.GetName()!));
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.SetConstraintName(Convert(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entityType.GetIndexes())
            {
                index.SetDatabaseName(Convert(index.GetDatabaseName()!));
            }
        }
    }
}
