using System.Net;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Frappe.EndToEnd.Tests;

/// <summary>
/// The image as it is deployed: built from the Dockerfile, migrated by its Migrator, with its Api and its Worker
/// running. Every wait is a bounded poll; a failure carries the logs of
/// the Migrator, the Api and the Worker.
/// </summary>
[ClassDataSource<DeployedFrappe>(Shared = SharedType.PerTestSession)]
public sealed class DeployedImageTests(DeployedFrappe stack)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [Test]
    public Task The_Migrator_finishes_successfully() => ExplainedAsync(async () =>
        await Assert.That(stack.MigratorExitCode).IsEqualTo(0L));

    [Test]
    public Task The_Api_and_the_Worker_become_ready() => ExplainedAsync(async () =>
    {
        var apiReady = await EventuallyAsync(async () =>
        {
            using var response = await stack.Api.GetAsync("/health/ready");
            return response.StatusCode == HttpStatusCode.OK;
        });
        var workerReady = await EventuallyAsync(async () =>
            (await stack.Worker.ExecAsync(["/app/worker/Frappe.Worker", "--probe", "tcp://localhost:8082"])).ExitCode == 0);

        await Assert.That(apiReady).IsTrue();
        await Assert.That(workerReady).IsTrue();
    });

    [Test]
    public Task The_hosts_run_as_the_non_root_app_user() => ExplainedAsync(async () =>
    {
        using var docker = new DockerClientBuilder().WithEndpoint(TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint).Build();
        var users = new List<string>();
        foreach (var host in stack.Hosts)
        {
            var inspection = await docker.Containers.InspectContainerAsync(host.Id);
            users.Add(inspection.Config?.User ?? string.Empty);
        }

        await Assert.That(users.Count).IsEqualTo(3);
        await Assert.That(users).All().Satisfy(user => user.IsEqualTo("app"));
    });

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }

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
