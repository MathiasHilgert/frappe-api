using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class ApiFixtureTests
{
    [Fact]
    public async Task DisposeAsync_WhenTheHostNeverStarted_DoesNotMaskTheStartupError()
    {
        // A failed InitializeAsync leaves Host unset; disposing must not replace the real error.
        var fixture = new ApiFixture();

        await Should.NotThrowAsync(async () => await fixture.DisposeAsync());
    }
}
