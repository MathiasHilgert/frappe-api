using System.Diagnostics.Metrics;
using Frappe.Access.Contracts;
using Frappe.Access.Infrastructure;
using Frappe.Access.UnitTests.Fixtures;
using Frappe.Results;
using Frappe.Testing;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Access.UnitTests;

/// <summary>
/// Access composed as a host composes it: the real module over the rules and the handler ports of the fixtures, one caller, a fake clock and
/// captured logs and metrics. Tests resolve the gate and the authorizer from a scope, as a request does.
/// </summary>
internal sealed class AccessWorld : IAsyncDisposable
{
    public static readonly Guid Principal = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public AccessWorld(ICaller? caller)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs));
        services.AddMetrics();
        services.AddSingleton<TimeProvider>(Time);
        if (caller is not null)
        {
            services.AddSingleton(caller);
        }

        services.AddSingleton<IRules, TestRules>();
        services.AddScoped<IQueryHandler<ReadNoteQuery, string>>(_ => throw new NotSupportedException());
        services.AddScoped<ICommandHandler<ChangeNoteCommand, Unit>>(_ => throw new NotSupportedException());
        services.AddScoped<ICommandHandler<EditNoteCommand, Unit>>(_ => throw new NotSupportedException());
        services.AddScoped<ICommandHandler<ExplodingGuardCommand, Unit>>(_ => throw new NotSupportedException());
        services.AddScoped<IQueryHandler<OpenPageQuery, string>>(_ => throw new NotSupportedException());
        services.AddScoped<ICommandHandler<UnmarkedCommand, Unit>>(_ => throw new NotSupportedException());
        new AccessCoreServices().AddServices(services);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Metrics = new MetricCapture(_provider.GetRequiredService<IMeterFactory>());
        _scope = _provider.CreateAsyncScope();
    }

    public CapturedLogs Logs { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

    public MetricCapture Metrics { get; }

    public IUseCaseGate Gate => _scope.ServiceProvider.GetRequiredService<IUseCaseGate>();

    public IAuthorizer Authorizer => _scope.ServiceProvider.GetRequiredService<IAuthorizer>();

    /// <summary>A signed-in caller of <paramref name="kind"/>, stepped up for <paramref name="stepUpFor"/> from now (or never).</summary>
    public static Caller Signed(Guid? principal = null, string kind = "owner", TimeSpan? stepUpFor = null, DateTimeOffset? now = null) =>
        new(
            principal ?? Principal,
            kind,
            Guid.NewGuid(),
            stepUpFor is { } window ? (now ?? new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero)) + window : null);

    public async ValueTask DisposeAsync()
    {
        Metrics.Dispose();
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        Logs.Dispose();
    }
}
