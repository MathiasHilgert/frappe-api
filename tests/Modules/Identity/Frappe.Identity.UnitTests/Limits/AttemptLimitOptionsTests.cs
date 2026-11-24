using Frappe.Identity.Limits.Valkey;

namespace Frappe.Identity.UnitTests.Limits;

/// <summary>The limit settings default to nothing set and name their full key when wrong (CFG-02, CFG-03).</summary>
public sealed class AttemptLimitOptionsTests
{
    [Test]
    public async Task No_setting_and_a_valid_one_are_accepted()
    {
        // Arrange
        var configured = new AttemptLimitOptions();
        configured.Flows["code.guess"] = new FlowLimitOptions { Limit = 3, Window = TimeSpan.FromMinutes(30) };

        // Act
        var results = (new AttemptLimitOptionsValidator().Validate(null, new AttemptLimitOptions()).Succeeded,
            new AttemptLimitOptionsValidator().Validate(null, configured).Succeeded);

        // Assert
        await Assert.That(results).IsEqualTo((true, true));
    }

    [Test]
    public async Task A_limit_below_one_and_a_window_out_of_range_are_refused_naming_every_key()
    {
        // Arrange
        var options = new AttemptLimitOptions();
        options.Flows["code.guess"] = new FlowLimitOptions { Limit = 0, Window = TimeSpan.Zero };
        options.Flows["code.send"] = new FlowLimitOptions { Window = AttemptLimitOptions.MaxWindow + TimeSpan.FromSeconds(1) };

        // Act
        var result = new AttemptLimitOptionsValidator().Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failures!.Count()).IsEqualTo(3);
            await Assert.That(result.FailureMessage).Contains("Identity:Limits:code.guess:Limit");
            await Assert.That(result.FailureMessage).Contains("Identity:Limits:code.guess:Window");
            await Assert.That(result.FailureMessage).Contains("Identity:Limits:code.send:Window");
        }
    }
}
