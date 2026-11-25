using Frappe.Notifications.Application.Messages;

namespace Frappe.Notifications.UnitTests.Kinds;

/// <summary>What a person is called in a mail: short, no link and nothing invisible (N3), as Identity's owner names.</summary>
public sealed class PayloadTextTests
{
    [Test]
    [Arguments("Ana Pérez")]
    [Arguments("Zoë")]
    public async Task A_plain_name_is_a_person_name(string name)
    {
        // Act
        var valid = PayloadText.IsPersonName(name);

        // Assert
        await Assert.That(valid).IsTrue();
    }

    [Test]
    [Arguments("Ana\u202Eevil")]
    [Arguments("Ana\u200Bx")]
    [Arguments("An\na")]
    [Arguments("Ana\u2028x")]
    [Arguments("Ana\u2029x")]
    [Arguments("\U000E0041na")]
    public async Task A_name_with_an_invisible_or_line_breaking_character_is_not_a_person_name(string name)
    {
        // Act
        var valid = PayloadText.IsPersonName(name);

        // Assert
        await Assert.That(valid).IsFalse();
    }
}
