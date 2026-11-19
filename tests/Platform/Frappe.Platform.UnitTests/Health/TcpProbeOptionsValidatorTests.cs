using Frappe.Health;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>The probe ports are checked at startup, every failure naming its full key.</summary>
public sealed class TcpProbeOptionsValidatorTests
{
    private readonly TcpProbeOptionsValidator _validator = new();

    [Test]
    public async Task Every_port_outside_1_to_65535_is_reported_in_one_failure_naming_each_full_key()
    {
        // Act
        var result = _validator.Validate(null, new TcpProbeOptions { LivePort = 0, ReadyPort = 65536, StartupPort = 8083 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(result.FailureMessage).Contains("Configuration 'Health:Probes:LivePort' must be a port from 1 to 65535, not '0'.");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Health:Probes:ReadyPort' must be a port from 1 to 65535, not '65536'.");
        }
    }

    [Test]
    public async Task The_defaults_and_the_edges_of_the_range_pass()
    {
        // Arrange
        var defaults = _validator.Validate(null, new TcpProbeOptions());

        // Act
        var edges = _validator.Validate(null, new TcpProbeOptions { LivePort = 1, ReadyPort = 65535, StartupPort = 8083 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(defaults.Succeeded).IsTrue();
            await Assert.That(edges.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task Two_probes_on_one_port_fail_naming_both_full_keys()
    {
        // Act
        var result = _validator.Validate(null, new TcpProbeOptions { LivePort = 8081, ReadyPort = 8083, StartupPort = 8083 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(1);
            await Assert.That(result.FailureMessage).Contains("Configuration 'Health:Probes:ReadyPort' and 'Health:Probes:StartupPort' must be different ports, not both '8083'.");
        }
    }
}
