namespace Frappe.Access.Contracts;

/// <summary>
/// Who runs the current use case: the principal and session a web request proved itself with, or <see cref="Caller.System"/> in the
/// Worker. It is injected where it is needed; the host decides how it is filled (the session scheme on the Api), and no host has a default,
/// so a host that forgot to register one fails when it resolves a handler. Facts are read live inside the request, never cached (SEC-17).
/// </summary>
public interface ICaller
{
    /// <summary>The principal the caller proved itself as; nothing for an anonymous or <see cref="Caller.System"/> caller.</summary>
    Guid? PrincipalId { get; }

    /// <summary>The principal's kind, such as <c>owner</c>; nothing when there is no principal.</summary>
    string? Kind { get; }

    /// <summary>The session the caller authenticated with; nothing when it did not.</summary>
    Guid? SessionId { get; }

    /// <summary>When the session's step-up stops counting; nothing when it never stepped up. The rule compares it with the clock.</summary>
    DateTimeOffset? StepUpValidUntil { get; }

    /// <summary>True for the platform itself (jobs and consumers). It is no principal and never passes a guarded rule implicitly.</summary>
    bool IsSystem { get; }

    /// <summary>The principal of a use case whose guard already required one; throws when there is none (a programming error).</summary>
    Guid RequiredPrincipalId => PrincipalId ?? throw new InvalidOperationException("A use case that requires a principal ran without one.");

    /// <summary>The session of a use case whose guard already required one; throws when there is none (a programming error).</summary>
    Guid RequiredSessionId => SessionId ?? throw new InvalidOperationException("A use case that requires a session ran without one.");
}
