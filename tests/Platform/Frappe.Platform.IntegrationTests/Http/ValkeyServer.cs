using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frappe.Testing.Containers;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>One real Valkey per test session: what the shared rate limit and the idempotency store count and keep in.</summary>
public sealed class ValkeyServer : IAsyncInitializer, IAsyncDisposable
{
    private const int Port = 6379;

    private readonly IContainer _container = new ContainerBuilder("valkey/valkey:8-alpine")
        .WithPortBinding(Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("valkey-cli", "ping"))
        .ReusedAs("valkey")
        .Build();

    /// <summary>The address the client connects to, as <c>host:port</c>.</summary>
    public string ConnectionString => $"{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    public async Task InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
