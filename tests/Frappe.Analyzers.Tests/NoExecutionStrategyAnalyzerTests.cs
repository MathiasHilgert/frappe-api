namespace Frappe.Analyzers.Tests;

/// <summary>FRP002 (EVT-21, PERF-02): EF Core's retrying execution strategies are never enabled.</summary>
public sealed class NoExecutionStrategyAnalyzerTests
{
    /// <summary>The shape of EF Core's and Npgsql's API that the rule looks at, without referencing either package.</summary>
    private const string EfCoreStubs = """
        namespace Microsoft.EntityFrameworkCore.Storage
        {
            /// <summary>A sample type.</summary>
            public interface IExecutionStrategy { bool RetriesOnFailure { get; } }
        }

        namespace Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure
        {
            /// <summary>A sample type.</summary>
            public sealed class NpgsqlDbContextOptionsBuilder
            {
                public NpgsqlDbContextOptionsBuilder EnableRetryOnFailure() => this;
                public NpgsqlDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount) => this;
                public NpgsqlDbContextOptionsBuilder UseNodaTime() => this;
            }
        }
        """;

    [Test]
    [Arguments("npgsql.EnableRetryOnFailure();")]
    [Arguments("npgsql.UseNodaTime().EnableRetryOnFailure(3);")]
    public async Task Enabling_retry_on_failure_is_reported(string statement)
    {
        // Arrange
        var source = $$"""
            using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

            /// <summary>A sample type.</summary>
            public static class Sample
            {
                public static void Configure(NpgsqlDbContextOptionsBuilder npgsql) { {{statement}} }
            }
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new NoExecutionStrategyAnalyzer(), EfCoreStubs, source);

        // Assert
        await Assert.That(diagnostics.Select(d => d.Id)).IsEquivalentTo(["FRP002"]);
    }

    [Test]
    public async Task A_custom_execution_strategy_is_reported()
    {
        // Arrange
        const string source = """
            /// <summary>A sample type.</summary>
            public sealed class Sample : Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy
            {
                public bool RetriesOnFailure => true;
            }
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new NoExecutionStrategyAnalyzer(), EfCoreStubs, source);

        // Assert
        await Assert.That(diagnostics.Select(d => d.Id)).IsEquivalentTo(["FRP002"]);
    }

    [Test]
    public async Task Configuring_the_provider_without_retries_is_not_reported()
    {
        // Arrange
        const string source = """
            using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

            /// <summary>A sample type.</summary>
            public static class Sample
            {
                public static void Configure(NpgsqlDbContextOptionsBuilder npgsql) => npgsql.UseNodaTime();
            }
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new NoExecutionStrategyAnalyzer(), EfCoreStubs, source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task A_method_of_our_own_named_EnableRetryOnFailure_is_not_reported()
    {
        // Arrange
        const string source = """
            /// <summary>A sample type.</summary>
            public sealed class Relay
            {
                public void EnableRetryOnFailure() { }

                public void Start() => EnableRetryOnFailure();
            }
            """;

        // Act
        var diagnostics = await AnalyzerRun.DiagnosticsOf(new NoExecutionStrategyAnalyzer(), source);

        // Assert
        await Assert.That(diagnostics).IsEmpty();
    }
}
