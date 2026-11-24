using System.Net;
using System.Net.Sockets;
using Frappe.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>The probe command against an HTTP target, and the TCP probe ports bound from configuration.</summary>
// FreePort() releases the port it found before the test binds it, so two tests running at once can get the same one: every test
// class that opens real TCP ports runs alone among them.
[NotInParallel("tcp-ports")]
public sealed class ProbeTargetTests
{
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<int> ProbeAsync(HttpStatusCode status)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var serving = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = (int)status;
            context.Response.Close();
        });

        var exit = await ProbeCommand.RunAsync($"http://localhost:{port}/health/ready");
        await serving;
        return exit;
    }

    [Test]
    public async Task An_http_target_is_healthy_only_on_a_success_status()
    {
        // Act
        var ok = await ProbeAsync(HttpStatusCode.OK);
        var unavailable = await ProbeAsync(HttpStatusCode.ServiceUnavailable);
        var nothingListening = await ProbeCommand.RunAsync($"http://localhost:{FreePort()}/health/ready");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(ok).IsEqualTo(0);
            await Assert.That(unavailable).IsEqualTo(1);
            await Assert.That(nothingListening).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments("tcp://localhost")]
    [Arguments("ftp://localhost:21")]
    [Arguments("file:///tmp/x")]
    public async Task A_target_without_a_port_or_with_an_unknown_scheme_exits_1_with_a_message(string target)
    {
        // Arrange
        var error = new StringWriter();

        // Act
        var exit = await ProbeCommand.RunAsync(target, error);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(exit).IsEqualTo(1);
            await Assert.That(error.ToString()).Contains(target);
        }
    }

    [Test]
    public async Task The_probe_ports_default_and_bind_from_the_Health_Probes_section()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Health:Probes:ReadyPort"] = "9182" })
            .Build();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddTcpHealthProbes()
            .BuildServiceProvider();

        // Act
        var options = services.GetRequiredService<IOptions<TcpProbeOptions>>().Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.ReadyPort).IsEqualTo(9182);
            await Assert.That(options.LivePort).IsEqualTo(TcpProbePorts.Default.Live);
            await Assert.That(options.StartupPort).IsEqualTo(TcpProbePorts.Default.Startup);
        }
    }

    [Test]
    public async Task The_probe_ports_given_in_code_are_the_options_the_probes_use()
    {
        // Arrange
        var ports = new TcpProbePorts(9101, 9102, 9103);
        await using var services = new ServiceCollection().AddLogging().AddTcpHealthProbes(ports).BuildServiceProvider();

        // Act
        var options = services.GetRequiredService<IOptions<TcpProbeOptions>>().Value;

        // Assert
        await Assert.That(new TcpProbePorts(options.LivePort, options.ReadyPort, options.StartupPort)).IsEqualTo(ports);
    }
}
