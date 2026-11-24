namespace Frappe.Identity.Application.Credentials;

/// <summary>How a step-up attempt ended, as <c>frappe.identity.step_ups</c> counts it.</summary>
public enum StepUpOutcome
{
    /// <summary>The password was right and the session stepped up.</summary>
    Completed,

    /// <summary>The password was wrong.</summary>
    Incorrect,

    /// <summary>The principal made more rechecks than the window allows.</summary>
    Throttled,
}
