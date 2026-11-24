using Frappe.Access.Contracts;

namespace Frappe.Access.UnitTests.Rules;

/// <summary>The pieces Access ships (S2): what each says about an owner, an anonymous caller and the system, through their rule contract.</summary>
public sealed class RulePiecesTests
{
    private static ValueTask<Verdict> StepUpAsync(TimeSpan window) =>
        Has.RecentStepUp.EvaluateAsync(RuleContexts.For(RuleContexts.Signed(stepUpFor: window)), CancellationToken.None);

    [Test]
    public async Task Authenticated_allows_a_signed_in_principal()
    {
        // Act
        var verdict = await Is.Authenticated.EvaluateAsync(RuleContexts.For(RuleContexts.Signed()), CancellationToken.None);

        // Assert
        await Assert.That(verdict.IsAllowed).IsTrue();
    }

    [Test]
    public async Task Authenticated_denies_an_anonymous_caller_and_the_system_with_not_authenticated()
    {
        // Act
        var anonymous = await Is.Authenticated.EvaluateAsync(RuleContexts.For(Caller.Anonymous), CancellationToken.None);
        var system = await Is.Authenticated.EvaluateAsync(RuleContexts.For(Caller.System), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(anonymous.Reason).IsEqualTo(DenialReason.NotAuthenticated);
            await Assert.That(system.Reason).IsEqualTo(DenialReason.NotAuthenticated);
        }
    }

    [Test]
    public async Task Kind_allows_that_kind_denies_another_and_never_passes_anonymous_or_system()
    {
        // Arrange
        var admin = Is.Kind("admin");

        // Act
        var same = await admin.EvaluateAsync(RuleContexts.For(RuleContexts.Signed("admin")), CancellationToken.None);
        var other = await admin.EvaluateAsync(RuleContexts.For(RuleContexts.Signed("owner")), CancellationToken.None);
        var anonymous = await admin.EvaluateAsync(RuleContexts.For(Caller.Anonymous), CancellationToken.None);
        var system = await admin.EvaluateAsync(RuleContexts.For(Caller.System), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(same.IsAllowed).IsTrue();
            await Assert.That(other.Reason).IsEqualTo(DenialReason.WrongKind);
            await Assert.That(anonymous.Reason).IsEqualTo(DenialReason.NotAuthenticated);
            await Assert.That(system.Reason).IsEqualTo(DenialReason.NotAuthenticated);
        }
    }

    [Test]
    public async Task Holder_allows_the_principal_the_resource_names_and_denies_anyone_else_the_system_and_a_missing_holder()
    {
        // Arrange
        var holder = AccessWorld.Principal;

        // Act
        var same = await Is.Holder.EvaluateAsync(RuleContexts.For(RuleContexts.Signed(), holder), CancellationToken.None);
        var stranger = RuleContexts.Signed(principal: AccessWorld.Stranger);
        var other = await Is.Holder.EvaluateAsync(RuleContexts.For(stranger, holder), CancellationToken.None);
        var system = await Is.Holder.EvaluateAsync(RuleContexts.For(Caller.System, holder), CancellationToken.None);
        var noResource = await Is.Holder.EvaluateAsync(RuleContexts.For(RuleContexts.Signed()), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(same.IsAllowed).IsTrue();
            await Assert.That(other.Reason).IsEqualTo(DenialReason.NotHolder);
            await Assert.That(system.Reason).IsEqualTo(DenialReason.NotHolder);
            await Assert.That(noResource.Reason).IsEqualTo(DenialReason.NotHolder);
        }
    }

    [Test]
    public async Task RecentStepUp_allows_only_while_the_window_is_open_by_the_clock_of_the_request()
    {
        // Act
        var open = await StepUpAsync(TimeSpan.FromMinutes(5));
        var ending = await StepUpAsync(TimeSpan.Zero);
        var elapsed = await StepUpAsync(TimeSpan.FromMinutes(-1));
        var never = await Has.RecentStepUp.EvaluateAsync(RuleContexts.For(RuleContexts.Signed()), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(open.IsAllowed).IsTrue();
            await Assert.That(ending.Reason).IsEqualTo(DenialReason.StepUpRequired);
            await Assert.That(elapsed.Reason).IsEqualTo(DenialReason.StepUpRequired);
            await Assert.That(never.Reason).IsEqualTo(DenialReason.StepUpRequired);
        }
    }

    [Test]
    public async Task RecentStepUp_never_passes_for_anonymous_or_system_even_with_an_open_window()
    {
        // Arrange
        var openWindow = RuleContexts.Now.AddHours(1);

        // Act
        var anonymous = await Has.RecentStepUp.EvaluateAsync(
            RuleContexts.For(new Caller(null, null, null, openWindow)), CancellationToken.None);
        var system = await Has.RecentStepUp.EvaluateAsync(
            RuleContexts.For(new Caller(null, null, null, openWindow, IsSystem: true)), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(anonymous.Reason).IsEqualTo(DenialReason.NotAuthenticated);
            await Assert.That(system.Reason).IsEqualTo(DenialReason.NotAuthenticated);
        }
    }

    [Test]
    public async Task The_caller_gives_its_required_ids_or_fails_loudly_when_it_has_none()
    {
        // Arrange
        ICaller signed = RuleContexts.Signed();
        ICaller anonymous = Caller.Anonymous;

        // Act
        var principal = signed.RequiredPrincipalId;
        var session = signed.RequiredSessionId;
        var noPrincipal = Assert.Throws<InvalidOperationException>(() => _ = anonymous.RequiredPrincipalId);
        var noSession = Assert.Throws<InvalidOperationException>(() => _ = anonymous.RequiredSessionId);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(principal).IsEqualTo(AccessWorld.Principal);
            await Assert.That(session).IsEqualTo(signed.SessionId!.Value);
            await Assert.That(noPrincipal).IsNotNull();
            await Assert.That(noSession).IsNotNull();
        }
    }
}
