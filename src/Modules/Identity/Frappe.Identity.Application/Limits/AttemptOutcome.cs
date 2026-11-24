namespace Frappe.Identity.Application.Limits;

/// <summary>What the limiter decided about one attempt.</summary>
public enum AttemptOutcome
{
    /// <summary>The attempt was counted and is within the limit.</summary>
    Allowed,

    /// <summary>The subject used its attempts for this window.</summary>
    Refused,

    /// <summary>The counter could not be reached and the flow fails closed: the attempt must be refused.</summary>
    Unavailable,
}
