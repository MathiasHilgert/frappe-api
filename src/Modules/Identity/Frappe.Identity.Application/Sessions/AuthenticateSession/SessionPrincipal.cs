using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.AuthenticateSession;

/// <summary>
/// Who a request proved to be: the principal of a live session, as that session and principal are right now. The kind and the methods are
/// the lower snake case codes clients and claims use, so the adapters that read them never depend on the domain.
/// </summary>
/// <param name="PrincipalId">The principal.</param>
/// <param name="Kind">What the principal is, as a code (<see cref="OwnerKind"/>).</param>
/// <param name="SessionId">The session the request presented.</param>
/// <param name="Methods">How the holder proved who they are when the session opened, as codes (<see cref="PasswordMethod"/>).</param>
/// <param name="LastSeenAt">When the session was last touched.</param>
/// <param name="StepUpUntil">Until when a recent password check counts, if one was made (it may already be past).</param>
public sealed record SessionPrincipal(
    Guid PrincipalId, string Kind, Guid SessionId, IReadOnlyList<string> Methods, Instant LastSeenAt, Instant? StepUpUntil)
{
    /// <summary>The kind code of an owner.</summary>
    public const string OwnerKind = "owner";

    /// <summary>The method code of a password.</summary>
    [SuppressMessage("Major Code Smell", "S2068:Credentials should not be hard-coded", Justification = "The name of a method, not a credential.")]
    public const string PasswordMethod = "pwd";

    /// <summary>A session is touched at most this often, so authenticating stays a read most of the time.</summary>
    public static readonly Duration TouchInterval = Duration.FromMinutes(1);

    /// <summary>Whether the session was last touched <see cref="TouchInterval"/> ago or more at <paramref name="now"/>.</summary>
    public bool IsDueForTouch(Instant now) => now - LastSeenAt >= TouchInterval;

    /// <summary>The principal a session lets in, from the facts that were read.</summary>
    internal static SessionPrincipal Of(SessionAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return new SessionPrincipal(
            access.PrincipalId, KindCode(access.Kind), access.SessionId, [.. access.Methods.Select(MethodCode)], access.LastSeenAt,
            access.StepUpUntil);
    }

    private static string KindCode(PrincipalKind kind) => kind switch
    {
        PrincipalKind.Owner => OwnerKind,
        _ => throw new UnreachableException($"Unknown principal kind {kind}."),
    };

    private static string MethodCode(AuthenticationMethod method) => method switch
    {
        AuthenticationMethod.Pwd => PasswordMethod,
        _ => throw new UnreachableException($"Unknown authentication method {method}."),
    };
}
