namespace Frappe.Access.Application;

/// <summary>How an access decision ended: the closed set of the <c>frappe.outcome</c> tag of <see cref="AccessMetrics.DecisionsMetric"/>.</summary>
internal enum AccessOutcome
{
    /// <summary>A guarded request, or an action on a resource, passed its rule.</summary>
    Allowed,

    /// <summary>The resource was hidden: answered with its own not-found.</summary>
    Hidden,

    /// <summary>A rule refused: 403 <c>access.action.forbidden</c>.</summary>
    Forbidden,

    /// <summary>The caller needs a recent step-up: 403 <c>access.step_up.required</c>.</summary>
    StepUpRequired,

    /// <summary>The request is public: anyone may run it, so no rule ran.</summary>
    Public,

    /// <summary>The request declares neither marker and ran anyway: a host refuses to start with one (S7), so this is a fallback.</summary>
    Undeclared,
}
