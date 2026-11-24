namespace Frappe.Identity.Application.Limits;

/// <summary>What a limit does when its counter cannot be reached (SEC-14).</summary>
public enum LimitFailure
{
    /// <summary>The attempt is refused: the limit guards a secret (code guesses), so an outage must not open brute force.</summary>
    FailClosed,

    /// <summary>The attempt goes through uncounted: the limit only slows sending or enumeration, so an outage must not stop the feature.</summary>
    FailOpen,
}
