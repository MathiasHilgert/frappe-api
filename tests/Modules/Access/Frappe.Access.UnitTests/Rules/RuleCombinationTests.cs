using Frappe.Access.Contracts;
using Frappe.Access.UnitTests.Fixtures;

namespace Frappe.Access.UnitTests.Rules;

/// <summary>How rules combine (S3, S9): <c>&amp;</c> binds tighter than <c>|</c>, every rule describes itself and the heaviest denial wins.</summary>
public sealed class RuleCombinationTests
{
    private static ValueTask<Verdict> EvaluateAsync(CallerRule rule, ICaller caller) =>
        rule.EvaluateAsync(RuleContexts.For(caller), CancellationToken.None);

    [Test]
    public async Task And_binds_tighter_than_or_so_A_or_B_and_C_means_A_or_the_pair()
    {
        // Arrange
        CallerRule unparenthesized = new AlwaysAllowed() | Is.Authenticated & Is.Kind("owner");
        CallerRule parenthesized = (new AlwaysAllowed() | Is.Authenticated) & Is.Kind("owner");

        // Act
        var first = await EvaluateAsync(unparenthesized, Caller.Anonymous);
        var second = await EvaluateAsync(parenthesized, Caller.Anonymous);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.IsAllowed).IsTrue();
            await Assert.That(second.IsAllowed).IsFalse();
        }
    }

    [Test]
    public async Task Describe_shows_the_combination_with_the_parentheses_precedence_needs()
    {
        // Act
        var descriptions = new[]
        {
            (Is.Authenticated & Has.RecentStepUp).Describe(),
            (Is.Kind("owner") | Is.Kind("admin")).Describe(),
            (Is.Kind("owner") | Is.Kind("admin") & Has.RecentStepUp).Describe(),
            ((Is.Kind("owner") | Is.Kind("admin")) & Has.RecentStepUp).Describe(),
        };

        // Assert
        await Assert.That(descriptions).IsEquivalentTo(
        [
            "Is.Authenticated & Has.RecentStepUp",
            "Is.Kind(owner) | Is.Kind(admin)",
            "Is.Kind(owner) | Is.Kind(admin) & Has.RecentStepUp",
            "(Is.Kind(owner) | Is.Kind(admin)) & Has.RecentStepUp",
        ]);
    }

    [Test]
    public async Task A_denial_of_a_kind_wins_over_a_missing_step_up_whatever_the_order_of_the_pieces()
    {
        // Arrange
        var owner = RuleContexts.Signed("owner");

        // Act
        var stepUpFirst = await EvaluateAsync(Has.RecentStepUp & Is.Kind("admin"), owner);
        var stepUpLast = await EvaluateAsync(Is.Kind("admin") & Has.RecentStepUp, owner);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stepUpFirst.Reason).IsEqualTo(DenialReason.WrongKind);
            await Assert.That(stepUpLast.Reason).IsEqualTo(DenialReason.WrongKind);
        }
    }

    [Test]
    public async Task A_missing_step_up_alone_is_reported_as_step_up_required()
    {
        // Act
        var verdict = await EvaluateAsync(Is.Authenticated & Has.RecentStepUp, RuleContexts.Signed());

        // Assert
        await Assert.That(verdict.Reason).IsEqualTo(DenialReason.StepUpRequired);
    }

    [Test]
    public async Task When_every_alternative_denies_the_one_closest_to_passing_is_reported()
    {
        // Act
        var verdict = await EvaluateAsync(Is.Kind("admin") | Has.RecentStepUp, RuleContexts.Signed());
        var reversed = await EvaluateAsync(Has.RecentStepUp | Is.Kind("admin"), RuleContexts.Signed());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(verdict.Reason).IsEqualTo(DenialReason.StepUpRequired);
            await Assert.That(reversed.Reason).IsEqualTo(DenialReason.StepUpRequired);
        }
    }

    [Test]
    public async Task A_piece_a_module_writes_combines_with_the_shipped_ones_without_touching_Access()
    {
        // Arrange
        CallerRule rule = Is.Authenticated & new AlwaysAllowed();

        // Act
        var signed = await EvaluateAsync(rule, RuleContexts.Signed());
        var anonymous = await EvaluateAsync(rule, Caller.Anonymous);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(signed.IsAllowed).IsTrue();
            await Assert.That(anonymous.Reason).IsEqualTo(DenialReason.NotAuthenticated);
        }
    }

    [Test]
    public async Task A_caller_rule_and_a_resource_rule_combine_into_a_resource_rule_that_hides_before_it_forbids()
    {
        // Arrange
        ResourceRule<Note> rule = Has.RecentStepUp & Is.Holder;
        var context = RuleContexts.For(RuleContexts.Signed(principal: AccessWorld.Stranger), holder: AccessWorld.Principal);

        // Act
        var verdict = await rule.EvaluateAsync(context, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(verdict.Reason).IsEqualTo(DenialReason.NotHolder);
            await Assert.That(rule.Describe()).IsEqualTo("Has.RecentStepUp & Is.Holder");
        }
    }
}
