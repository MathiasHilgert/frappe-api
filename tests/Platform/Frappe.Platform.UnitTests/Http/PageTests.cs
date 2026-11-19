using Frappe.Http;
using Frappe.Results;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HTTP-14: the platform page: limit 1 to 200 (default 50), an opaque cursor bound to what it was issued for.</summary>
public sealed class PageTests
{
    /// <summary>A sample keyset position: a name and the id that breaks ties.</summary>
    private sealed record Position(string Name, Guid Id);

    private static readonly Guid RowId = Guid.Parse("0197d7b0-0000-7000-8000-000000000001");

    [Test]
    public async Task Without_a_limit_or_a_cursor_the_page_starts_at_the_beginning_with_the_default_limit()
    {
        // Act
        var request = PageRequest.Bind<Position>(null, null, "scope");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(request.IsSuccess).IsTrue();
            await Assert.That(request.Value.Limit).IsEqualTo(PageLimits.Default).And.IsEqualTo(50);
            await Assert.That(request.Value.After).IsNull();
        }
    }

    [Test]
    [Arguments("1", 1)]
    [Arguments("50", 50)]
    [Arguments("200", 200)]
    public async Task A_limit_from_one_to_the_maximum_is_taken_as_it_is(string raw, int expected)
    {
        // Act
        var request = PageRequest.Bind<Position>(raw, null, "scope");

        // Assert
        await Assert.That(request.Value.Limit).IsEqualTo(expected);
    }

    [Test]
    [Arguments("0", "platform.page_limit.out_of_range")]
    [Arguments("201", "platform.page_limit.out_of_range")]
    [Arguments("99999999999", "platform.page_limit.invalid")]
    [Arguments("ten", "platform.page_limit.invalid")]
    [Arguments("-1", "platform.page_limit.invalid")]
    [Arguments("1.5", "platform.page_limit.invalid")]
    [Arguments("", "platform.page_limit.invalid")]
    public async Task A_limit_outside_the_bounds_or_not_a_whole_number_is_an_invalid_value_with_the_bounds_as_parameters(string raw, string code)
    {
        // Arrange
        var request = PageRequest.Bind<Position>(raw, null, "scope");

        // Act
        var error = request.Errors.Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(error).IsTypeOf<InvalidValueError>();
            await Assert.That(error.Code).IsEqualTo(code);
            await Assert.That(error.Parameters["value"]).IsEqualTo(raw);
            await Assert.That(error.Parameters["min"]).IsEqualTo(1);
            await Assert.That(error.Parameters["max"]).IsEqualTo(200);
        }
    }

    [Test]
    public async Task The_cursor_of_a_page_brings_the_next_one_back_to_the_position_it_ended_at()
    {
        // Arrange
        var page = Page.Of(["a", "b"], new Position("b", RowId), "scope");

        // Act
        var request = PageRequest.Bind<Position>("2", page.NextCursor, "scope");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(page.NextCursor).IsNotNull();
            await Assert.That(request.Value.After).IsEqualTo(new Position("b", RowId));
        }
    }

    [Test]
    public async Task The_last_page_has_no_cursor_and_items_can_be_projected_keeping_it()
    {
        // Arrange
        var last = Page.Of(["a"], (Position?)null, "scope");

        // Act
        var next = Page.Of(["a"], new Position("a", RowId), "scope").Select(item => item.ToUpperInvariant());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(last.NextCursor).IsNull();
            await Assert.That(next.Items).IsEquivalentTo(["A"]);
            await Assert.That(next.NextCursor).IsNotNull();
        }
    }

    [Test]
    public async Task A_cursor_is_only_valid_for_the_scope_it_was_issued_for()
    {
        // Arrange
        var cursor = Page.Of(["a"], new Position("a", RowId), "scope").NextCursor;

        // Act
        var request = PageRequest.Bind<Position>(null, cursor, "another scope");

        // Assert
        await Assert.That(request.Errors.Single().Code).IsEqualTo("platform.page_cursor.invalid");
    }

    [Test]
    [Arguments("")]
    [Arguments("not a cursor")]
    [Arguments("e30")]
    [Arguments("W10")]
    [Arguments("eyJ2IjoiazEifQ")]
    public async Task A_cursor_that_is_not_one_this_platform_issued_is_an_invalid_value(string raw)
    {
        // Arrange
        var request = PageRequest.Bind<Position>(null, raw, "scope");

        // Act
        var error = request.Errors.Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(error).IsTypeOf<InvalidValueError>();
            await Assert.That(error.Code).IsEqualTo("platform.page_cursor.invalid");
        }
    }

    [Test]
    public async Task A_bad_limit_and_a_bad_cursor_are_both_told()
    {
        // Act
        var request = PageRequest.Bind<Position>("ten", "nope", "scope");

        // Assert
        await Assert.That(request.Errors.Select(e => e.Code)).IsEquivalentTo(["platform.page_limit.invalid", "platform.page_cursor.invalid"]);
    }

    [Test]
    public async Task A_cursor_whose_position_the_list_refuses_is_an_invalid_cursor()
    {
        // Arrange
        var forged = Page.Of([1], new Position("zzz", RowId), "scope").NextCursor!;

        var refused = PageRequest.Bind<Position>(null, forged, "scope", position => position.Name != "zzz");

        // Act
        var accepted = PageRequest.Bind<Position>(null, forged, "scope", position => position.Name == "zzz");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refused.Errors.Single().Code).IsEqualTo("platform.page_cursor.invalid");
            await Assert.That(accepted.Value.After).IsEqualTo(new Position("zzz", RowId));
        }
    }
}
