namespace Frappe.Access.Contracts;

/// <summary>The rules about what the caller has proved or holds.</summary>
public static class Has
{
    /// <summary>
    /// The session's step-up window is still open by the clock of the request. The instant the host read is compared with the clock here, never
    /// trusted alone. A caller without a principal fails it as not authenticated.
    /// </summary>
    public static CallerRule RecentStepUp { get; } = new SimpleRule(
        "Has.RecentStepUp",
        context => context.Caller switch
        {
            { IsSystem: true } or { PrincipalId: null } => Verdict.Deny(DenialReason.NotAuthenticated),
            { StepUpValidUntil: { } until } when until > context.Now => Verdict.Allowed,
            _ => Verdict.Deny(DenialReason.StepUpRequired),
        });
}
