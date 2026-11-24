namespace Frappe.Access.Contracts;

/// <summary>Why a rule denied: a fixed set, so no denial carries text, ids or rule internals (S3, S10).</summary>
public enum DenialReason
{
    /// <summary>The caller has no principal.</summary>
    NotAuthenticated,

    /// <summary>The principal is not of the kind the rule asks for.</summary>
    WrongKind,

    /// <summary>The caller is not the resource's holder: the resource is hidden from it.</summary>
    NotHolder,

    /// <summary>The session has no open step-up window: repeating the request after a step-up may pass.</summary>
    StepUpRequired,

    /// <summary>A piece of a module said no for a reason of its own.</summary>
    NotAllowed,
}
