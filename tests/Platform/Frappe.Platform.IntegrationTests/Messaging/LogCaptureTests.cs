using Microsoft.Extensions.Logging;

namespace Frappe.Platform.IntegrationTests.Messaging;

public sealed class LogCaptureTests
{
    [Fact]
    public void A_template_naming_a_placeholder_twice_is_captured_instead_of_throwing()
    {
        using LogCapture capture = new();
        ILogger logger = capture.CreateLogger("test");

#pragma warning disable CA1848, CA1873, CA2017 // the duplicate placeholder is the case under test
        logger.LogInformation("{Id} then {Id}", 1, 2);
#pragma warning restore CA1848, CA1873, CA2017

        capture.Records.ShouldHaveSingleItem().State["Id"].ShouldBe(2);
    }
}
