using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Domain;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// Whoever can act in the system. Owns its credential, its sessions and its pending codes, so every
/// sign-in rule is checked in one consistency boundary. Secrets arrive already hashed.
/// </summary>
public sealed class Principal : AggregateRoot<Guid>
{
    private readonly List<Session> _sessions = [];
    private readonly List<VerificationCode> _codes = [];

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

    public static Principal RegisterOwner(PersonName name, Email email, string passwordHash, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var principal = new Principal(new OwnerAccount(name, email, passwordHash), clock.GetUtcNow());
        principal.Raise(new OwnerRegistrationSubmitted(principal.Id, email.Value));
        return principal;
    }

    /// <summary>Issues a code for <paramref name="purpose"/>, replacing the previous one so only one is live.</summary>
    public Result<VerificationCode> IssueCode(CodePurpose purpose, string codeHash, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);

        var now = clock.GetUtcNow();
        var previous = CodeFor(purpose);
        var issued = previous is null ? Result.Success(VerificationCode.First(purpose, codeHash, now)) : previous.Resend(codeHash, now);
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

    public void Suspend() => Owner.Status = OwnerStatus.Suspended;

    public Result<Session> OpenSession(string tokenHash, SessionSurface surface, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        var notification = new Notification()
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

    public Result ConfirmEmailChange(string codeHash, TimeProvider clock)
    {
        if (Owner.PendingEmail is not { } newEmail)
        {
            return Result.Failure(IdentityErrors.NoPendingEmailChange);
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

    private VerificationCode? CodeFor(CodePurpose purpose) => _codes.Find(c => c.Purpose == purpose);

    // Hashes are compared, never codes: the caller hashes what the person typed with the same port.
    private Result ConsumeCode(CodePurpose purpose, string codeHash, TimeProvider clock)
    {
        var code = CodeFor(purpose);
        return code is null || !string.Equals(code.CodeHash, codeHash, StringComparison.Ordinal)
            ? Result.Failure(IdentityErrors.CodeInvalid)
            : code.Consume(clock.GetUtcNow());
    }

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
