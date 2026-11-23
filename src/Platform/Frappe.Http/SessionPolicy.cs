using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;

namespace Frappe.Http;

/// <summary>
/// What every module needs to guard a route with a session: the name of the authorization policy and the claim that names
/// the principal. The scheme that proves a session, and the handler that issues the claim, belong to the module that authenticates users, which
/// registers the policy under <see cref="Name"/>; a module only asks for it.
/// </summary>
public static class SessionPolicy
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Requires an authenticated session (<see cref="Name"/>); the module that registers the policy decides how a
        /// missing or unverifiable session is answered.
        /// </summary>
        public TBuilder RequireSession()
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.RequireAuthorization(Name);
            return builder;
        }
    }

    /// <summary>The authorization policy of the routes that require a session.</summary>
    public const string Name = "Session";

    /// <summary>The claim with the principal's id.</summary>
    public const string PrincipalClaim = "sub";

    /// <summary>
    /// The claim with the instant (round-trip ISO 8601) the session's step-up, a re-presentation of the account password,
    /// stops counting. Absent when the session never stepped up or its step-up has run out. That module issues it, and a module
    /// that guards a sensitive write reads it through <see cref="StepUpValidUntilOf"/> and compares it with its own clock.
    /// </summary>
    public const string StepUpUntilClaim = "step_up_until";

    /// <summary>The <see cref="StepUpUntilClaim"/> a session that stepped up until <paramref name="validUntil"/> carries.</summary>
    public static Claim StepUpClaim(DateTimeOffset validUntil) =>
        new(StepUpUntilClaim, validUntil.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// When the step-up of the session an authenticated request proved itself with stops counting; nothing when the user never
    /// authenticated (a claim alone proves nothing), the session never stepped up, or the claim is not an instant. It is the
    /// instant as the request authenticated: a caller still compares it with its own clock before it relies on it.
    /// </summary>
    public static DateTimeOffset? StepUpValidUntilOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(user.FindFirstValue(StepUpUntilClaim), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
            out var validUntil)
            ? validUntil
            : null;
    }

    /// <summary>
    /// The id of the principal an authenticated request proved itself as; nothing when it did not: the user never
    /// authenticated (a claim alone proves nothing), or has no claim, or the claim is not an id.
    /// </summary>
    public static Guid? PrincipalIdOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        return Guid.TryParse(user.FindFirstValue(PrincipalClaim), out var principal) ? principal : null;
    }

    /// <summary>
    /// The principal of a route guarded by <see cref="RequireSession{TBuilder}"/>, where authorization already refused a request without one;
    /// a request that reaches the handler without it is a wiring defect, not a client error.
    /// </summary>
    public static Guid RequiredPrincipalIdOf(ClaimsPrincipal user) =>
        PrincipalIdOf(user) ?? throw new InvalidOperationException("A route that requires a session ran without one.");
}
