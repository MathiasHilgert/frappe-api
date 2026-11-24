using Frappe.Access.Contracts;
using Frappe.Access.UnitTests.Fixtures;
using Frappe.Results;
using Microsoft.Extensions.Logging;

namespace Frappe.Access.UnitTests.Gate;

/// <summary>
/// The use-case gate of Access composed as a host does (S2, S9, S10): what a guarded, a public and an undeclared request are answered, the
/// signals each decision leaves and the obligation of a request that acts on a resource.
/// </summary>
public sealed class AccessGateTests
{
    private const string Decisions = "frappe.access.decisions";

    private static async Task<(string Request, string Outcome)[]> DecisionsOf(AccessWorld world)
    {
        await Task.CompletedTask;
        return [.. world.Metrics.Of(Decisions).Select(m => ((string)m.Tags["request"]!, (string)m.Tags["frappe.outcome"]!))];
    }

    [Test]
    public async Task A_guarded_request_whose_rule_passes_runs_and_counts_allowed()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Gate.AdmitAsync(new ReadNoteQuery(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsNull();
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("ReadNoteQuery", "allowed")]);
        }
    }

    [Test]
    public async Task An_anonymous_caller_never_passes_a_guarded_request_and_is_answered_forbidden_never_unauthenticated()
    {
        // Arrange
        await using var world = new AccessWorld(Caller.Anonymous);

        // Act
        var refusal = await world.Gate.AdmitAsync(new ReadNoteQuery(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsEqualTo(new ForbiddenError("access.action.forbidden"));
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("ReadNoteQuery", "forbidden")]);
        }
    }

    [Test]
    public async Task The_system_caller_never_passes_a_guarded_request_implicitly()
    {
        // Arrange
        await using var world = new AccessWorld(Caller.System);

        // Act
        var refusal = await world.Gate.AdmitAsync(new ReadNoteQuery(), CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsEqualTo(new ForbiddenError("access.action.forbidden"));
    }

    [Test]
    public async Task A_request_that_needs_a_step_up_is_answered_step_up_required_until_the_window_opens_and_after_it_closes()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed(stepUpFor: TimeSpan.FromMinutes(10)));

        // Act
        var open = await world.Gate.AdmitAsync(new ChangeNoteCommand(), CancellationToken.None);
        await world.Gate.CompleteAsync(new ChangeNoteCommand(), succeeded: true, CancellationToken.None);
        world.Time.Advance(TimeSpan.FromMinutes(11));
        var closed = await world.Gate.AdmitAsync(new ChangeNoteCommand(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(open).IsNull();
            await Assert.That(closed).IsEqualTo(new ForbiddenError("access.step_up.required"));
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("ChangeNoteCommand", "allowed"), ("ChangeNoteCommand", "step_up_required")]);
        }
    }

    [Test]
    public async Task A_public_request_passes_for_anyone_and_counts_public()
    {
        // Arrange
        await using var world = new AccessWorld(Caller.Anonymous);

        // Act
        var refusal = await world.Gate.AdmitAsync(new OpenPageQuery(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsNull();
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("OpenPageQuery", "public")]);
        }
    }

    [Test]
    public async Task An_undeclared_request_passes_for_now_is_counted_and_is_logged_by_name()
    {
        // Arrange
        await using var world = new AccessWorld(Caller.Anonymous);

        // Act
        var refusal = await world.Gate.AdmitAsync(new UnmarkedCommand(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsNull();
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("UnmarkedCommand", "undeclared")]);
            await Assert.That(world.Logs.Entries.Any(
                e => e.Level == LogLevel.Information && e.Message.Contains("UnmarkedCommand", StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task A_rule_that_throws_refuses_the_request_and_leaks_nothing_of_the_exception()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Gate.AdmitAsync(new ExplodingGuardCommand(), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsEqualTo(new ForbiddenError("access.action.forbidden"));
            await Assert.That(await DecisionsOf(world)).IsEquivalentTo([("ExplodingGuardCommand", "forbidden")]);
            await Assert.That(world.Logs.Entries.Any(
                e => e.Level == LogLevel.Warning && e.Message.Contains("ExplodingGuardCommand", StringComparison.Ordinal)))
                .IsTrue();
            await Assert.That(world.Logs.Entries.Any(e => e.Message.Contains("user@example.com", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task A_denial_is_logged_at_information_with_the_request_and_the_fixed_reason_and_no_ids()
    {
        // Arrange
        var caller = AccessWorld.Signed();
        await using var world = new AccessWorld(caller);

        // Act
        await world.Gate.AdmitAsync(new ChangeNoteCommand(), CancellationToken.None);

        // Assert
        var entry = world.Logs.Entries.Single(e => e.Level == LogLevel.Information);
        using (Assert.Multiple())
        {
            await Assert.That(entry.Message).Contains("ChangeNoteCommand").And.Contains("StepUpRequired");
            await Assert.That(entry.Message).DoesNotContain(caller.PrincipalId!.Value.ToString()).And.DoesNotContain("Is.Authenticated");
        }
    }

    [Test]
    public async Task A_request_that_acts_on_a_resource_and_succeeds_without_checking_it_fails_closed()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());
        await world.Gate.AdmitAsync(new EditNoteCommand(), CancellationToken.None);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await world.Gate.CompleteAsync(new EditNoteCommand(), succeeded: true, CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("EditNoteCommand").And.Contains("Note");
    }

    [Test]
    public async Task A_request_that_acts_on_a_resource_and_checks_it_completes_even_when_the_check_refuses()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());
        await world.Gate.AdmitAsync(new EditNoteCommand(), CancellationToken.None);
        var refusal = await world.Authorizer.RefuseAsync(NoteActions.Read, new Note(AccessWorld.Stranger), CancellationToken.None);

        // Act
        await world.Gate.CompleteAsync(new EditNoteCommand(), succeeded: true, CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsNotNull();
    }

    [Test]
    public async Task A_request_that_acts_on_a_resource_and_fails_owes_no_check()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());
        await world.Gate.AdmitAsync(new EditNoteCommand(), CancellationToken.None);

        // Act
        var completion = world.Gate.CompleteAsync(new EditNoteCommand(), succeeded: false, CancellationToken.None);

        // Assert
        await Assert.That(completion.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task A_request_run_inside_another_in_the_same_scope_keeps_the_outer_obligation()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());
        await world.Gate.AdmitAsync(new EditNoteCommand(), CancellationToken.None);
        await world.Gate.AdmitAsync(new OpenPageQuery(), CancellationToken.None);
        await world.Gate.CompleteAsync(new OpenPageQuery(), succeeded: true, CancellationToken.None);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await world.Gate.CompleteAsync(new EditNoteCommand(), succeeded: true, CancellationToken.None));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task A_host_with_no_caller_fails_when_it_resolves_the_gate()
    {
        // Act
        var failure = Assert.Throws<AggregateException>(() => _ = new AccessWorld(caller: null));

        // Assert
        await Assert.That(failure.Message).Contains("ICaller");
    }
}
