using Frappe.Platform.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Health;

public sealed class DependencyRegistrationTests
{
    [Theory]
    [InlineData(Criticality.Critical, HealthStatus.Unhealthy, HealthTags.Critical)]
    [InlineData(Criticality.NonCritical, HealthStatus.Degraded, HealthTags.NonCritical)]
    public void A_typed_dependency_fails_with_the_status_and_tag_of_its_criticality(
        Criticality criticality,
        HealthStatus failureStatus,
        string tag
    )
    {
        HealthCheckRegistration registration = SingleRegistration(checks =>
            checks.AddDependency<HealthyCheck>("database", criticality)
        );

        registration.Name.ShouldBe("database");
        registration.FailureStatus.ShouldBe(failureStatus);
        registration.Tags.ShouldBe([tag], ignoreOrder: true);
    }

    [Theory]
    [InlineData(Criticality.Critical, HealthStatus.Unhealthy, HealthTags.Critical)]
    [InlineData(Criticality.NonCritical, HealthStatus.Degraded, HealthTags.NonCritical)]
    public void A_factory_dependency_fails_with_the_status_and_tag_of_its_criticality(
        Criticality criticality,
        HealthStatus failureStatus,
        string tag
    )
    {
        HealthCheckRegistration registration = SingleRegistration(checks =>
            checks.AddDependency("cache", criticality, static _ => new HealthyCheck())
        );

        registration.Name.ShouldBe("cache");
        registration.FailureStatus.ShouldBe(failureStatus);
        registration.Tags.ShouldBe([tag], ignoreOrder: true);
    }

    [Fact]
    public void A_dependency_without_a_timeout_gets_two_seconds()
    {
        HealthCheckRegistration registration = SingleRegistration(static checks =>
            checks.AddDependency<HealthyCheck>("database", Criticality.Critical)
        );

        registration.Timeout.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void An_explicit_timeout_is_kept()
    {
        HealthCheckRegistration registration = SingleRegistration(static checks =>
            checks.AddDependency(
                "mail",
                Criticality.NonCritical,
                static _ => new HealthyCheck(),
                TimeSpan.FromMilliseconds(500)
            )
        );

        registration.Timeout.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Extra_tags_are_added_next_to_the_criticality_tag()
    {
        HealthCheckRegistration registration = SingleRegistration(static checks =>
            checks.AddDependency<HealthyCheck>(
                "database",
                Criticality.Critical,
                tags: [HealthTags.Startup, "postgres"]
            )
        );

        registration.Tags.ShouldBe(
            [HealthTags.Critical, HealthTags.Startup, "postgres"],
            ignoreOrder: true
        );
    }

    [Fact]
    public void A_typed_dependency_is_built_from_the_container()
    {
        ServiceCollection services = new();
        services.AddSingleton(new Endpoint("db.internal"));
        services.AddHealthChecks().AddDependency<EndpointCheck>("database", Criticality.Critical);
        using ServiceProvider provider = services.BuildServiceProvider();
        HealthCheckRegistration registration = Registrations(provider).ShouldHaveSingleItem();

        IHealthCheck check = registration.Factory(provider);

        check.ShouldBeOfType<EndpointCheck>().Endpoint.Host.ShouldBe("db.internal");
    }

    [Fact]
    public void A_criticality_outside_the_defined_values_is_rejected()
    {
        IHealthChecksBuilder checks = new ServiceCollection().AddHealthChecks();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            checks.AddDependency<HealthyCheck>("database", (Criticality)7)
        );
    }

    [Theory]
    [InlineData(HealthTags.Live)]
    [InlineData(HealthTags.Critical)]
    [InlineData(HealthTags.NonCritical)]
    public void A_typed_dependency_rejects_a_reserved_tag(string tag)
    {
        IHealthChecksBuilder checks = new ServiceCollection().AddHealthChecks();

        Should
            .Throw<ArgumentException>(() =>
                checks.AddDependency<HealthyCheck>("database", Criticality.NonCritical, tags: tag)
            )
            .ParamName.ShouldBe("tags");
    }

    [Theory]
    [InlineData(HealthTags.Live)]
    [InlineData(HealthTags.Critical)]
    [InlineData(HealthTags.NonCritical)]
    public void A_factory_dependency_rejects_a_reserved_tag(string tag)
    {
        IHealthChecksBuilder checks = new ServiceCollection().AddHealthChecks();

        Should
            .Throw<ArgumentException>(() =>
                checks.AddDependency(
                    "cache",
                    Criticality.Critical,
                    static _ => new HealthyCheck(),
                    tags: tag
                )
            )
            .ParamName.ShouldBe("tags");
    }

    [Theory]
    [InlineData(Criticality.Critical, HealthStatus.Unhealthy)]
    [InlineData(Criticality.NonCritical, HealthStatus.Degraded)]
    public async Task A_dependency_that_outlives_its_timeout_reports_its_failure_status_on_time(
        Criticality criticality,
        HealthStatus failureStatus
    )
    {
        ServiceCollection services = new();
        services.AddLogging();
        services
            .AddHealthChecks()
            .AddDependency(
                "slow",
                criticality,
                static _ => new HangingCheck(),
                TimeSpan.FromMilliseconds(100)
            );
        await using ServiceProvider provider = services.BuildServiceProvider();

        HealthReport report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        report.Entries["slow"].Status.ShouldBe(failureStatus);
        report.TotalDuration.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    private static HealthCheckRegistration SingleRegistration(Action<IHealthChecksBuilder> register)
    {
        ServiceCollection services = new();
        register(services.AddHealthChecks());
        using ServiceProvider provider = services.BuildServiceProvider();
        return Registrations(provider).ShouldHaveSingleItem();
    }

    private static ICollection<HealthCheckRegistration> Registrations(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

    private sealed class HealthyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(HealthCheckResult.Healthy());
    }

    private sealed class HangingCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return HealthCheckResult.Healthy();
        }
    }

    private sealed record Endpoint(string Host);

    private sealed class EndpointCheck(Endpoint endpoint) : IHealthCheck
    {
        public Endpoint Endpoint { get; } = endpoint;

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(HealthCheckResult.Healthy());
    }
}
