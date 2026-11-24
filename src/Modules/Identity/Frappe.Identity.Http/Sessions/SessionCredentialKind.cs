namespace Frappe.Identity.Http.Sessions;

/// <summary>What a request presents as its session credential.</summary>
internal enum SessionCredentialKind
{
    /// <summary>Nothing: an anonymous request.</summary>
    None,

    /// <summary>An <c>Authorization: Bearer</c> header; it wins over a cookie.</summary>
    Bearer,

    /// <summary>The session cookie, once.</summary>
    Cookie,

    /// <summary>The session cookie more than once: a fixation attempt, never authenticated (SEC-02).</summary>
    AmbiguousCookie,

    /// <summary>More than one <c>Authorization</c> header: which one counts is not for the server to guess.</summary>
    AmbiguousBearer,
}
