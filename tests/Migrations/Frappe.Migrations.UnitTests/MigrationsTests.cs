using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Frappe.Migrations.UnitTests;

public sealed class MigrationsTests
{
    // Migrations are forward-only; EF scaffolds a Down method that has to be deleted.
    [Fact]
    public void No_migration_defines_a_down_step()
    {
        string[] withDown =
        [
            .. typeof(MigrationsDbContextFactory)
                .Assembly.GetTypes()
                .Where(static type =>
                    type.IsSubclassOf(typeof(Migration))
                    && type.GetMethod(
                        "Down",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        [typeof(MigrationBuilder)]
                    )?.DeclaringType != typeof(Migration)
                )
                .Select(static type => type.Name),
        ];

        withDown.ShouldBeEmpty();
    }
}
