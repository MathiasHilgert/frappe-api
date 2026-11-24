namespace Frappe.Identity.Application.Credentials;

/// <summary>How an email verification ended, as the verifications counter tags it.</summary>
public enum VerificationOutcome
{
    /// <summary>The code was right and the owner is active.</summary>
    Verified,

    /// <summary>The code, or the owner it was sent for, was not valid.</summary>
    Invalid,

    /// <summary>Guesses could not be counted, so none was judged.</summary>
    Unavailable,
}
