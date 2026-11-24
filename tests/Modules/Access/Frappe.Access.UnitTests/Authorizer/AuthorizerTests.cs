using Frappe.Access.Contracts;
using Frappe.Access.UnitTests.Fixtures;
using Frappe.Results;
using Microsoft.Extensions.Logging;

namespace Frappe.Access.UnitTests.Authorizer;

/// <summary>
/// The resource check of Access composed as a host does (S9, S10): the precedence hidden, forbidden, step-up whatever the rule order, and
/// fail-closed answers for what has no rule or cannot decide.
/// </summary>
public sealed class AuthorizerTests
{
    private static readonly NotFoundError NoteHidden = new(TestRules.NoteNotFound);

    private static readonly NotFoundError FolderHidden = new(TestRules.FolderNotFound);

    [Test]
    public async Task The_holder_may_do_what_its_rule_allows_and_the_decision_is_counted()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Authorizer.RefuseAsync(NoteActions.Read, new Note(AccessWorld.Principal), CancellationToken.None);

        // Assert
        var decision = world.Metrics.Of("frappe.access.decisions").Single();
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsNull();
            await Assert.That(decision.Tags["frappe.outcome"]).IsEqualTo("allowed");
            await Assert.That(decision.Tags["request"]).IsEqualTo("none");
        }
    }

    [Test]
    public async Task A_caller_who_does_not_hold_the_resource_gets_its_own_not_found_for_every_action()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed(principal: AccessWorld.Stranger));
        var note = new Note(AccessWorld.Principal);

        // Act
        var read = await world.Authorizer.RefuseAsync(NoteActions.Read, note, CancellationToken.None);
        var change = await world.Authorizer.RefuseAsync(NoteActions.Change, note, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(read).IsEqualTo(NoteHidden);
            await Assert.That(change).IsEqualTo(NoteHidden);
            await Assert.That(world.Metrics.Of("frappe.access.decisions").Select(m => (string)m.Tags["frappe.outcome"]!))
                .IsEquivalentTo(["hidden", "hidden"]);
        }
    }

    [Test]
    public async Task Anonymous_and_system_callers_never_see_a_held_resource()
    {
        // Arrange
        await using var anonymous = new AccessWorld(Caller.Anonymous);
        await using var system = new AccessWorld(Caller.System);
        var note = new Note(AccessWorld.Principal);

        // Act
        var anonymousAnswer = await anonymous.Authorizer.RefuseAsync(NoteActions.Read, note, CancellationToken.None);
        var systemAnswer = await system.Authorizer.RefuseAsync(NoteActions.Read, note, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(anonymousAnswer).IsEqualTo(NoteHidden);
            await Assert.That(systemAnswer).IsEqualTo(NoteHidden);
        }
    }

    [Test]
    public async Task A_holder_without_a_step_up_is_answered_step_up_required()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Authorizer.RefuseAsync(NoteActions.Change, new Note(AccessWorld.Principal), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsEqualTo(new ForbiddenError("access.step_up.required"));
            await Assert.That(world.Metrics.Of("frappe.access.decisions").Single().Tags["frappe.outcome"]).IsEqualTo("step_up_required");
        }
    }

    [Test]
    public async Task A_visible_resource_whose_action_the_caller_may_not_do_is_forbidden_before_it_is_step_up_whatever_the_rule_order()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed(kind: "owner"));
        var folder = new Folder(AccessWorld.Principal);

        // Act
        var stepUpFirst = await world.Authorizer.RefuseAsync(FolderActions.ArchiveStepUpFirst, folder, CancellationToken.None);
        var stepUpLast = await world.Authorizer.RefuseAsync(FolderActions.ArchiveStepUpLast, folder, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stepUpFirst).IsEqualTo(new ForbiddenError("access.action.forbidden"));
            await Assert.That(stepUpLast).IsEqualTo(new ForbiddenError("access.action.forbidden"));
        }
    }

    [Test]
    public async Task A_holder_rule_inside_an_action_hides_the_resource_instead_of_forbidding_it()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed(principal: AccessWorld.Stranger));

        // Act
        var refusal = await world.Authorizer.RefuseAsync(FolderActions.Edit, new Folder(AccessWorld.Principal), CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsEqualTo(FolderHidden);
    }

    [Test]
    public async Task An_anonymous_caller_fails_visibility_and_is_answered_not_found_never_unauthenticated()
    {
        // Arrange
        await using var world = new AccessWorld(Caller.Anonymous);

        // Act
        var refusal = await world.Authorizer.RefuseAsync(FolderActions.Edit, new Folder(AccessWorld.Principal), CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsEqualTo(FolderHidden);
    }

    [Test]
    public async Task An_action_the_book_never_allowed_is_hidden()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Authorizer.RefuseAsync(NoteActions.Undeclared, new Note(AccessWorld.Principal), CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsEqualTo(NoteHidden);
    }

    [Test]
    public async Task A_resource_type_nobody_declared_is_hidden_under_the_generic_code()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Authorizer.RefuseAsync(new ResourceAction<Orphan>("any"), new Orphan(1), CancellationToken.None);

        // Assert
        await Assert.That(refusal).IsEqualTo(new NotFoundError("access.resource.not_found"));
    }

    [Test]
    public async Task A_rule_that_throws_hides_the_resource_and_the_log_carries_only_the_exception_type()
    {
        // Arrange
        await using var world = new AccessWorld(AccessWorld.Signed());

        // Act
        var refusal = await world.Authorizer.RefuseAsync(FolderActions.Explode, new Folder(AccessWorld.Principal), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refusal).IsEqualTo(FolderHidden);
            await Assert.That(world.Logs.Entries.Any(
                e => e.Level == LogLevel.Warning && e.Message.Contains("InvalidOperationException", StringComparison.Ordinal)))
                .IsTrue();
            await Assert.That(world.Logs.Entries.Any(e => e.Message.Contains("user@example.com", StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task A_refusal_inside_a_use_case_is_counted_under_the_request_and_logged_without_ids()
    {
        // Arrange
        var caller = AccessWorld.Signed(principal: AccessWorld.Stranger);
        await using var world = new AccessWorld(caller);
        await world.Gate.AdmitAsync(new EditNoteCommand(), CancellationToken.None);

        // Act
        await world.Authorizer.RefuseAsync(NoteActions.Read, new Note(AccessWorld.Principal), CancellationToken.None);

        // Assert
        var hidden = world.Metrics.Of("frappe.access.decisions").Single(m => (string)m.Tags["frappe.outcome"]! == "hidden");
        var entry = world.Logs.Entries.Single(e => e.Level == LogLevel.Information);
        using (Assert.Multiple())
        {
            await Assert.That(hidden.Tags["request"]).IsEqualTo("EditNoteCommand");
            await Assert.That(entry.Message).Contains("EditNoteCommand").And.Contains("Hidden");
            await Assert.That(entry.Message).DoesNotContain(AccessWorld.Principal.ToString()).And.DoesNotContain(AccessWorld.Stranger.ToString());
        }
    }

    [Test]
    public async Task Declaring_a_resource_twice_is_a_startup_error()
    {
        // Arrange
        var book = new RuleBook();
        new TestRules().Define(book);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => book.For<Note>("test.note.not_found"));

        // Assert
        await Assert.That(failure.Message).Contains("Note");
    }
}
