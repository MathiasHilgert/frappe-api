namespace Frappe.Identity.Http.Sessions;

/// <summary>The session body every route that opens one answers with. The token is shown here once; only its hash is stored.</summary>
/// <param name="Id">The session.</param>
/// <param name="Token">The opaque token the client sends as <c>Authorization: Bearer</c> (the console cookie carries the same one).</param>
/// <param name="IdleExpiresAt">The session idles out when it is not used by then.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
public sealed record OpenedSessionResponse(Guid Id, string Token, DateTimeOffset IdleExpiresAt, DateTimeOffset AbsoluteExpiresAt)
{
    /// <summary>Masked: the token is a secret (SEC-07, DOM-06).</summary>
    public override string ToString() => $"OpenedSessionResponse {{ Id = {Id}, Token = *** }}";
}
