using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Npgsql;

namespace Frappe.Testing.Containers;

/// <summary>
/// The one place that decides whether a test container is kept between local runs. Reuse is opt-in: only
/// <c>FRAPPE_TEST_REUSE=1</c> turns it on, and it never applies when <c>CI=true</c>, so CI always starts fresh containers.
/// A reused container is named by a label made of the worktree, the test app and the fixture, so two worktrees (or two
/// test apps) never share one. Under reuse run only one test session per test app per worktree at a time: two at once share
/// one container, and the reset of one (which drops every database, or clears Mailpit) clobbers the other. Reused containers
/// are not removed by Ryuk: stop them with <c>just test reuse-clean</c>.
/// </summary>
public static class TestReuse
{
    public const string Variable = "FRAPPE_TEST_REUSE";

    public const string Label = "frappe.tests.reuse";

    /// <summary>Whether containers are kept between runs.</summary>
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable(Variable) == "1" && Environment.GetEnvironmentVariable("CI") is not "true";

    /// <summary>
    /// Marks the container reusable under <paramref name="fixture"/> when reuse is on; leaves the builder untouched otherwise.
    /// </summary>
    public static TBuilder ReusedAs<TBuilder, TContainer, TConfiguration>(
        this IContainerBuilder<TBuilder, TContainer, TConfiguration> builder, string fixture)
        where TBuilder : IContainerBuilder<TBuilder, TContainer, TConfiguration>
        where TContainer : IContainer
        where TConfiguration : IContainerConfiguration =>
        Enabled ? builder.WithReuse(true).WithLabel(Label, KeyOf(fixture)) : (TBuilder)builder;

    /// <summary>
    /// A credential for <paramref name="purpose"/>: random per run, except under reuse, where the container's configuration
    /// (which holds it) must be the same on every run for the container to be found again. Never a literal in the repository.
    /// </summary>
    public static string Secret(string purpose, int bytes) =>
        Enabled
            ? Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(KeyOf(purpose))))[..(bytes * 2)]
            : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>
    /// Drops every database a crashed previous run left in a reused Postgres (all but the system ones); a no-op for a fresh one.
    /// </summary>
    public static async Task ResetPostgresAsync(string connectionString)
    {
        if (!Enabled)
        {
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        var leftovers = new List<string>();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using (var list = new NpgsqlCommand(
            "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> current_database()", connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                leftovers.Add(reader.GetString(0));
            }
        }

        foreach (var name in leftovers)
        {
            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static string KeyOf(string fixture)
    {
        var app = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "tests";
        var worktree = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(RepositoryRoot.FullPath)))[..10];
        return $"{worktree}.{app}.{fixture}";
    }
}
