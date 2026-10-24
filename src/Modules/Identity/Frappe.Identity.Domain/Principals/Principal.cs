using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Domain;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// Whoever can act in the system. Owns its credential, its sessions and its pending codes, so every
/// sign-in rule is checked in one consistency boundary. Secrets arrive already hashed.
/// </summary>
public sealed class Principal : AggregateRoot<Guid>
{
    /// <summary>How long an unverified registration may wait for its code before it is purged.</summary>
    public static readonly TimeSpan PendingGracePeriod = TimeSpan.FromDays(7);

    private readonly List<Session> _sessions = [];
    private readonly List<VerificationCode> _codes = [];

    private Principal() => Owner = null!;

    private Principal(OwnerAccount owner, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Owner = owner;
        RegisteredAt = now;
    }

    public PrincipalType Type { get; } = PrincipalType.Owner;
    public OwnerAccount Owner { get; }
    public DateTimeOffset RegisteredAt { get; }
    public IReadOnlyList<Session> Sessions => _sessions;
    public IReadOnlyList<VerificationCode> Codes => _codes;

    public static Principal RegisterOwner(PersonName name, Email email, string passwordHash, Locale locale, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(locale);

        var principal = new Principal(new OwnerAccount(name, email, passwordHash, locale), clock.GetUtcNow());
        principal.Raise(new OwnerRegistrationSubmitted(principal.Id, email.Value));
        return principal;
    }

    /// <summary>Issues a code for <paramref name="purpose"/>, replacing the previous one so only one is live.</summary>
    public Result<VerificationCode> IssueCode(CodePurpose purpose, string codeHash, TimeProvider clock) =>
        IssueCode(Guid.CreateVersion7(clock.GetUtcNow()), purpose, codeHash, clock);

    /// <summary>
    /// Issues a code whose id the caller chose, for flows that hand the id out before the code exists (a password
    /// reset answers at once and issues in the background, so its timing reveals nothing).
    /// </summary>
    public Result<VerificationCode> IssueCode(Guid codeId, CodePurpose purpose, string codeHash, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);

        var now = clock.GetUtcNow();
        var issued = VerificationCode.Replacing(CodeFor(purpose), codeId, purpose, codeHash, now);
        if (issued.IsSuccess)
        {
            _codes.RemoveAll(c => c.Purpose == purpose);
            _codes.Add(issued.Value);
        }
        return issued;
    }

    public Result VerifyEmail(string codeHash, TimeProvider clock)
    {
        if (Owner.Status != OwnerStatus.Pending)
        {
            return Result.Failure(IdentityErrors.AccountNotPending);
        }

        var consumed = ConsumeCode(CodePurpose.EmailVerify, codeHash, clock);
        if (consumed.IsSuccess)
        {
            Owner.Status = OwnerStatus.Active;
            Raise(new OwnerEmailVerified(Id));
        }
        return consumed;
    }

    /// <summary>
    /// Agrees to delete an unverified registration past its grace. Verified or suspended owners, and pending
    /// ones still inside the grace, are never purged.
    /// </summary>
    public Result PurgeAbandonedRegistration(TimeProvider clock)
    {
        if (Owner.Status != OwnerStatus.Pending)
        {
            return Result.Failure(IdentityErrors.AccountNotPending);
        }
        if (clock.GetUtcNow() - RegisteredAt <= PendingGracePeriod)
        {
            return Result.Failure(IdentityErrors.RegistrationInGrace);
        }

        Raise(new OwnerPendingPurged(Id));
        return Result.Success();
    }

    /// <summary>Which locales the platform serves is not identity's rule: the caller passes one it already accepted.</summary>
    public void ChangeLocale(Locale locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        Owner.Locale = locale;
    }

    /// <summary>Shuts the owner out at once; their sessions stop authenticating. Suspending twice is refused.</summary>
    public Result Suspend()
    {
        if (Owner.Status == OwnerStatus.Suspended)
        {
            return Result.Failure(IdentityErrors.AccountSuspended);
        }

        Owner.Status = OwnerStatus.Suspended;
        Raise(new OwnerSuspended(Id));
        return Result.Success();
    }

    public Result<Session> OpenSession(string tokenHash, SessionSurface surface, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var notification = new ValidationNotification()
            .AddIf(Owner.Status == OwnerStatus.Pending, IdentityErrors.AccountPending)
            .AddIf(Owner.Status == OwnerStatus.Suspended, IdentityErrors.AccountSuspended);

        return Result.From(notification, () =>
        {
            var session = new Session(tokenHash, surface, clock.GetUtcNow());
            _sessions.Add(session);
            Raise(new SessionOpened(Id, session.Id, surface));
            return session;
        });
    }

    /// <summary>Records use of a live session. An expired one is reported, never revived.</summary>
    public Result TouchSession(Guid sessionId, TimeProvider clock)
    {
        var session = _sessions.Find(s => s.Id == sessionId);
        if (session is null)
        {
            return Result.Failure(IdentityErrors.SessionNotFound);
        }
        if (!session.IsLive(clock))
        {
            return Result.Failure(IdentityErrors.SessionExpired);
        }

        session.Touch(clock.GetUtcNow());
        return Result.Success();
    }

    /// <summary>
    /// Records that the person behind a live session just re-proved their password (the caller verified it). Sensitive
    /// operations then accept that session until <see cref="Session.StepUpValidity"/> has passed.
    /// </summary>
    public Result<DateTimeOffset> CompleteStepUp(Guid sessionId, TimeProvider clock)
    {
        var session = _sessions.Find(s => s.Id == sessionId);
        if (session is null)
        {
            return Result.Failure<DateTimeOffset>(IdentityErrors.SessionNotFound);
        }
        if (!session.IsLive(clock))
        {
            return Result.Failure<DateTimeOffset>(IdentityErrors.SessionExpired);
        }

        return Result.Success(session.StepUp(clock.GetUtcNow()));
    }

    public bool HasFreshStepUp(Guid sessionId, TimeProvider clock) =>
        _sessions.Exists(s => s.Id == sessionId && s.HasFreshStepUp(clock));

    public Result RevokeSession(Guid sessionId)
    {
        var session = _sessions.Find(s => s.Id == sessionId);
        if (session is null)
        {
            return Result.Failure(IdentityErrors.SessionNotFound);
        }

        Revoke(session);
        return Result.Success();
    }

    /// <summary>A signed-in change: the session that made it survives, every other one is revoked.</summary>
    public void ChangePassword(string newPasswordHash, Guid currentSessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        RevokeSessionsExcept(currentSessionId);
        Owner.PasswordHash = newPasswordHash;
        Raise(new OwnerPasswordChanged(Id));
    }

    /// <summary>
    /// Replaces the stored hash with a stronger hash of the same password (the caller just proved it). The credential
    /// does not change, so sessions survive and nothing is announced.
    /// </summary>
    public void RehashPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        Owner.PasswordHash = passwordHash;
    }

    /// <summary>A signed-out change proven by a reset code: no session survives it.</summary>
    public Result ResetPassword(string codeHash, string newPasswordHash, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);

        var consumed = ConsumeCode(CodePurpose.PasswordReset, codeHash, clock);
        if (consumed.IsSuccess)
        {
            RevokeSessionsExcept(currentSessionId: null);
            Owner.PasswordHash = newPasswordHash;
            Raise(new OwnerPasswordChanged(Id));
        }
        return consumed;
    }

    public Result<VerificationCode> RequestEmailChange(Email newEmail, string codeHash, TimeProvider clock)
    {
        var issued = IssueCode(CodePurpose.EmailChange, codeHash, clock);
        if (issued.IsSuccess)
        {
            Owner.PendingEmail = newEmail;
            Raise(new OwnerEmailChangeRequested(Id, newEmail.Value));
        }
        return issued;
    }

    /// <summary>Swaps the email once the code sent to the new address comes back. <paramref name="changeId"/> is the live code's id.</summary>
    public Result ConfirmEmailChange(Guid changeId, string codeHash, TimeProvider clock)
    {
        if (Owner.PendingEmail is not { } newEmail)
        {
            return Result.Failure(IdentityErrors.NoPendingEmailChange);
        }
        if (CodeFor(CodePurpose.EmailChange)?.Id != changeId)
        {
            return Result.Failure(IdentityErrors.EmailChangeNotFound);
        }

        var consumed = ConsumeCode(CodePurpose.EmailChange, codeHash, clock);
        if (consumed.IsSuccess)
        {
            var oldEmail = Owner.Email;
            Owner.Email = newEmail;
            Owner.PendingEmail = null;
            Raise(new OwnerEmailChangeConfirmed(Id, oldEmail.Value, newEmail.Value));
        }
        return consumed;
    }

    /// <summary>
    /// Whether <paramref name="codeHash"/> matches the live, unused code for <paramref name="purpose"/>, without using
    /// it up. Lets a caller prove the code before paying for anything expensive.
    /// </summary>
    public Result CheckCode(CodePurpose purpose, string codeHash, TimeProvider clock) =>
        VerificationCode.Check(CodeFor(purpose), codeHash, clock.GetUtcNow());

    private VerificationCode? CodeFor(CodePurpose purpose) => _codes.Find(c => c.Purpose == purpose);

    private Result ConsumeCode(CodePurpose purpose, string codeHash, TimeProvider clock) =>
        VerificationCode.Consume(CodeFor(purpose), codeHash, clock.GetUtcNow());

    private void RevokeSessionsExcept(Guid? currentSessionId)
    {
        foreach (var session in _sessions.Where(s => s.Id != currentSessionId).ToList())
        {
            Revoke(session);
        }
    }

    private void Revoke(Session session)
    {
        _sessions.Remove(session);
        Raise(new SessionRevoked(Id, session.Id));
    }
}
