using System.Net;
using System.Text.Json;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using Frappe.Testing;

namespace Frappe.EndToEnd.Tests;

/// <summary>
/// The image as it is deployed: built from the Dockerfile, migrated by its Migrator, with its Api and its Worker
/// running. Every wait is a bounded poll; a failure carries the logs of the Migrator, the Api and the Worker.
/// </summary>
[ClassDataSource<DeployedFrappe>(Shared = SharedType.PerTestSession)]
public sealed class DeployedImageTests(DeployedFrappe stack)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [Test]
    public Task The_Migrator_finishes_successfully_and_provisions_the_scheduler() => ExplainedAsync(async () =>
    {
        // Act
        var exitCode = stack.MigratorExitCode;
        var role = await stack.Postgres.ExecAsync(
            ["psql", "-U", "frappe_owner", "-d", "frappe", "-tAc", "SELECT count(*) FROM pg_roles WHERE rolname = 'frappe_scheduler'"]);
        var tables = await stack.Postgres.ExecAsync(
            ["psql", "-U", "frappe_owner", "-d", "frappe", "-tAc", "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'quartz'"]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exitCode).IsEqualTo(0L);
            await Assert.That(role.Stdout.Trim()).IsEqualTo("1");
            await Assert.That(long.Parse(tables.Stdout.Trim(), System.Globalization.CultureInfo.InvariantCulture)).IsGreaterThan(0L);
        }
    });

    [Test]
    public Task The_Api_and_the_Worker_become_ready() => ExplainedAsync(async () =>
    {
        // Act
        await WaitUntil.TrueAsync(() => ApiReadyAsync(stack), "the Api never answered /health/ready with 200", Patience);
        await WaitUntil.TrueAsync(() => WorkerReadyAsync(stack), "the Worker never answered its readiness probe", Patience);
        var apiReady = await ApiReadyAsync(stack);
        var workerReady = await WorkerReadyAsync(stack);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(apiReady).IsTrue();
            await Assert.That(workerReady).IsTrue();
        }
    });

    [Test]
    public Task The_Api_serves_its_OpenAPI_document_with_its_own_title() => ExplainedAsync(async () =>
    {
        // Act
        using var response = await stack.Api.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(document.RootElement.GetProperty("info").GetProperty("title").GetString()).IsEqualTo("Frappé API");
        }
    });

    [Test]
    public Task The_hosts_run_as_the_non_root_app_user() => ExplainedAsync(async () =>
    {
        // Arrange
        using var docker = new DockerClientBuilder().WithEndpoint(TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint).Build();
        var users = new List<string>();

        // Act
        foreach (var host in stack.Hosts)
        {
            var inspection = await docker.Containers.InspectContainerAsync(host.Id);
            users.Add(inspection.Config?.User ?? string.Empty);
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(users.Count).IsEqualTo(3);
            await Assert.That(users).All().Satisfy(user => user.IsEqualTo("app"));
        }
    });

    private static async Task<bool> ApiReadyAsync(DeployedFrappe stack)
    {
        try
        {
            using var response = await stack.Api.GetAsync("/health/ready");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            // Not listening yet.
            return false;
        }
    }

    private static async Task<bool> WorkerReadyAsync(DeployedFrappe stack) =>
        (await stack.Worker.ExecAsync(["/app/worker/Frappe.Worker", "--probe", "tcp://localhost:8082"])).ExitCode == 0;

    /// <summary>Runs the assertions; a failure carries the hosts' logs.</summary>
    private async Task ExplainedAsync(Func<Task> assertions)
    {
        try
        {
            await assertions();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(exception.Message + await stack.LogsAsync(), exception);
        }
    }
}
