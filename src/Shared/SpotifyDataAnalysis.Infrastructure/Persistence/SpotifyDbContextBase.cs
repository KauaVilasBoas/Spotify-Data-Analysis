using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SpotifyDataAnalysis.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for all SpotifyDataAnalysis modules.
/// Each module inherits this and registers its configurations via OnModelCreating.
///
/// It provides one cross-cutting concern for every module: <b>snake_case</b> naming for all tables,
/// columns, keys and indexes, so the PostgreSQL schema is idiomatic (lowercase, underscore-separated)
/// and the Dapper read-side can target the same column names without quoting camelCase identifiers.
///
/// <para>The system is single-tenant: there is no global company/tenant query filter here. If per-user
/// data isolation is introduced later, add it as an explicit query filter on the owning entities and a
/// stamping interceptor — kept out of the base until the domain requires it.</para>
/// </summary>
public abstract class SpotifyDbContextBase : DbContext
{
    protected SpotifyDbContextBase(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ApplySnakeCaseNamingConvention(modelBuilder);
    }

    private static void ApplySnakeCaseNamingConvention(ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } tableName)
                entity.SetTableName(ToSnakeCase(tableName));

            // Owned entity types (value objects mapped to the owner's table via table splitting)
            // share the principal's primary key column. Their non-key columns are already named
            // explicitly by each configuration (HasColumnName), and their shared key/FK columns must
            // keep the principal's column name — renaming them here would split the shared column into
            // a divergent name and break the mapping. So the whole snake_case pass is skipped for owned types.
            if (entity.IsOwned())
                continue;

            foreach (IMutableProperty property in entity.GetProperties())
            {
                if (property.GetColumnName() is { } columnName)
                    property.SetColumnName(ToSnakeCase(columnName));
            }

            foreach (IMutableKey key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));

            foreach (IMutableForeignKey fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName() ?? string.Empty));

            foreach (IMutableIndex index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
        }
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        var snakeCaseChars = new List<char>(name.Length + 4);

        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];

            if (char.IsUpper(current))
            {
                bool isFirstChar = i == 0;
                bool previousIsLower = i > 0 && char.IsLower(name[i - 1]);
                bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (!isFirstChar && (previousIsLower || nextIsLower))
                    snakeCaseChars.Add('_');

                snakeCaseChars.Add(char.ToLowerInvariant(current));
            }
            else
            {
                snakeCaseChars.Add(current);
            }
        }

        return new string(snakeCaseChars.ToArray());
    }
}
