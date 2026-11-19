using Frappe.ModuleName.Application.AreaName.UseCaseName;

namespace Frappe.ModuleName.UnitTests.AreaName;

/// <summary>
/// FILL IN: say which rules of UseCaseName these tests pin, through the handler over fakes.
/// </summary>
public sealed class UseCaseNameTests
{
    [Test]
    public async Task Fill_in_the_rule_this_test_pins()
    {
        // Arrange
        var handler = new UseCaseNameHandler();

        // Act
        var result = await handler.HandleAsync(new UseCaseName(Guid.NewGuid()), CancellationToken.None);

        // Assert
        await Assert.That(result.IsSuccess).IsTrue();
    }
}
