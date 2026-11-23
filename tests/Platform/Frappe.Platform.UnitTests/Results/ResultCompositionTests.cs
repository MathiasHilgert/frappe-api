using Frappe.Results;

namespace Frappe.Platform.UnitTests.Results;

/// <summary>Then, Map and Ensure chain results: a success flows on, a failure short-circuits with its errors untouched (S5).</summary>
public sealed class ResultCompositionTests
{
    private static readonly Error First = Error.InvalidValue("orders.order.not_found");
    private static readonly Error Second = Error.RuleViolation("orders.order.already_shipped");

    private static Result<int> Failed(params Error[] errors) => Result.Failure<int>(errors);

    [Test]
    public async Task Then_runs_the_next_step_on_a_success()
    {
        // Act
        var result = Result.Success(2).Then(value => Result.Success(value * 10));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value).IsEqualTo(20);
        }
    }

    [Test]
    public async Task Then_returns_the_failure_of_the_next_step()
    {
        // Act
        var result = Result.Success(2).Then(_ => Failed(Second));

        // Assert
        await Assert.That(result.Errors).IsEquivalentTo([Second]);
    }

    [Test]
    public async Task Then_skips_the_next_step_on_a_failure_and_keeps_every_error()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = Failed(First, Second).Then(value =>
        {
            calls++;
            return Result.Success(value);
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(result.Errors).IsEquivalentTo([First, Second]);
        }
    }

    [Test]
    public async Task Then_awaits_an_async_next_step_on_a_success()
    {
        // Act
        var result = await Result.Success(2).Then(value => Task.FromResult(Result.Success(value + 1)));

        // Assert
        await Assert.That(result.Value).IsEqualTo(3);
    }

    [Test]
    public async Task Then_skips_an_async_next_step_on_a_failure()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = await Failed(First).Then(value =>
        {
            calls++;
            return Task.FromResult(Result.Success(value));
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(result.Errors).IsEquivalentTo([First]);
        }
    }

    [Test]
    public async Task Map_transforms_the_value_of_a_success()
    {
        // Act
        var result = Result.Success(2).Map(value => $"#{value}");

        // Assert
        await Assert.That(result.Value).IsEqualTo("#2");
    }

    [Test]
    public async Task Map_skips_the_function_on_a_failure_and_keeps_its_errors()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = Failed(First, Second).Map(value =>
        {
            calls++;
            return $"{value}";
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(result.Errors).IsEquivalentTo([First, Second]);
        }
    }

    [Test]
    public async Task Map_awaits_an_async_function_on_a_success_and_skips_it_on_a_failure()
    {
        // Arrange
        var calls = 0;

        // Act
        var success = await Result.Success(2).Map(async value =>
        {
            await Task.Yield();
            return value * 2;
        });
        var failure = await Failed(First).Map(value =>
        {
            calls++;
            return Task.FromResult(value);
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(success.Value).IsEqualTo(4);
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(failure.Errors).IsEquivalentTo([First]);
        }
    }

    [Test]
    public async Task Ensure_keeps_a_success_the_predicate_accepts()
    {
        // Act
        var result = Result.Success(5).Ensure(value => value > 0, First);

        // Assert
        await Assert.That(result.Value).IsEqualTo(5);
    }

    [Test]
    public async Task Ensure_turns_a_success_the_predicate_rejects_into_the_error()
    {
        // Act
        var result = Result.Success(-1).Ensure(value => value > 0, First);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors).IsEquivalentTo([First]);
        }
    }

    [Test]
    public async Task Ensure_keeps_an_existing_failure_unchanged_without_calling_the_predicate()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = Failed(Second).Ensure(_ =>
        {
            calls++;
            return false;
        }, First);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(result.Errors).IsEquivalentTo([Second]);
        }
    }

    [Test]
    public async Task A_pending_result_chains_through_Then_Map_and_Ensure()
    {
        // Arrange
        var pending = Task.FromResult(Result.Success(3));

        // Act
        var result = await pending
            .Then(value => Result.Success(value + 1))
            .Then(value => Task.FromResult(Result.Success(value * 2)))
            .Map(value => value + 1)
            .Map(value => Task.FromResult(value + 1))
            .Ensure(value => value > 100, First);

        // Assert
        await Assert.That(result.Errors).IsEquivalentTo([First]);
    }

    [Test]
    public async Task A_pending_failure_short_circuits_every_later_step_with_its_errors()
    {
        // Arrange
        var calls = 0;

        // Act
        var result = await Task.FromResult(Failed(First, Second))
            .Then(value =>
            {
                calls++;
                return Result.Success(value);
            })
            .Then(value =>
            {
                calls++;
                return Task.FromResult(Result.Success(value));
            })
            .Map(value =>
            {
                calls++;
                return value;
            })
            .Map(value =>
            {
                calls++;
                return Task.FromResult(value);
            })
            .Ensure(_ =>
            {
                calls++;
                return false;
            }, Error.RuleViolation("never"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(result.Errors).IsEquivalentTo([First, Second]);
        }
    }

    [Test]
    public async Task A_pending_success_flows_to_the_final_value()
    {
        // Act
        var result = await Task.FromResult(Result.Success(3))
            .Then(value => Result.Success(value + 1))
            .Map(value => value * 2)
            .Ensure(value => value == 8, First);

        // Assert
        await Assert.That(result.Value).IsEqualTo(8);
    }
}
