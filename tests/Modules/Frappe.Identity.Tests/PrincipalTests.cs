using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Domain;
using Shouldly;

namespace Frappe.Identity.Tests;

public sealed class PrincipalTests
{
    private readonly TestClock _clock = new();

    [Fact]
    public void RegisterOwner_StartsPendingAndRaisesRegistrationSubmitted()
    {
        var principal = Register();

        principal.Id.Version.ShouldBe(7);
        principal.Type.ShouldBe(PrincipalType.Owner);
        principal.Owner.Name.Value.ShouldBe("Ana");
        principal.Owner.Email.Value.ShouldBe("ana@example.com");
        principal.Owner.PasswordHash.ShouldBe("pw-hash");
        principal.Owner.Status.ShouldBe(OwnerStatus.Pending);
        principal.Owner.PendingEmail.ShouldBeNull();
        principal.RegisteredAt.ShouldBe(_clock.Now);
        Events(principal).ShouldBe([new OwnerRegistrationSubmitted(principal.Id, "ana@example.com")]);
    }

    [Fact]
    public void VerifyEmail_WithLiveCode_ActivatesAndRaisesEmailVerified()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c1");

        var result = principal.VerifyEmail("c1", _clock);

        result.IsSuccess.ShouldBeTrue();
        principal.Owner.Status.ShouldBe(OwnerStatus.Active);
        principal.Codes.Single().UsedAt.ShouldBe(_clock.Now);
        Events(principal).ShouldBe([new OwnerEmailVerified(principal.Id)]);
    }

    [Fact]
    public void VerifyEmail_WrongCode_ReportsCodeInvalidAndStaysPending()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c1");

        principal.VerifyEmail("other", _clock).Errors.ShouldBe([IdentityErrors.CodeInvalid]);

        principal.Owner.Status.ShouldBe(OwnerStatus.Pending);
        Events(principal).ShouldBeEmpty();
    }

    [Fact]
    public void VerifyEmail_AfterFifteenMinutes_ReportsCodeExpired()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c1");
        _clock.Advance(VerificationCode.Lifetime + TimeSpan.FromSeconds(1));

        principal.VerifyEmail("c1", _clock).Errors.ShouldBe([IdentityErrors.CodeExpired]);
        principal.Owner.Status.ShouldBe(OwnerStatus.Pending);
    }

    [Fact]
    public void VerifyEmail_CodeOfAnotherPurpose_ReportsCodeInvalid()
    {
        var principal = RegisterWithCode(CodePurpose.PasswordReset, "c1");

        principal.VerifyEmail("c1", _clock).Errors.ShouldBe([IdentityErrors.CodeInvalid]);
    }

    [Fact]
    public void VerifyEmail_AlreadyActive_ReportsAccountNotPending()
    {
        var principal = ActiveOwner();
        principal.IssueCode(CodePurpose.EmailVerify, "c2", _clock);

        principal.VerifyEmail("c2", _clock).Errors.ShouldBe([IdentityErrors.AccountNotPending]);
    }

    [Fact]
    public void ConsumeCode_Twice_ReportsCodeAlreadyUsed()
    {
        var principal = ActiveOwner();
        principal.IssueCode(CodePurpose.PasswordReset, "r1", _clock);
        principal.ResetPassword("r1", "new-hash", _clock);

        principal.ResetPassword("r1", "newer-hash", _clock).Errors.ShouldBe([IdentityErrors.CodeAlreadyUsed]);
        principal.Owner.PasswordHash.ShouldBe("new-hash");
    }

    [Fact]
    public void IssueCode_Again_ReplacesTheLiveCodeAndCountsTheResend()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c1");

        var code = principal.IssueCode(CodePurpose.EmailVerify, "c2", _clock).Value;

        code.ResendCount.ShouldBe(1);
        principal.Codes.ShouldHaveSingleItem().ShouldBeSameAs(code);
        principal.VerifyEmail("c1", _clock).Errors.ShouldBe([IdentityErrors.CodeInvalid]);
        principal.VerifyEmail("c2", _clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void IssueCode_BeyondResendLimitWithinAnHour_ReportsResendLimitReached()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c0");
        for (var i = 1; i <= VerificationCode.MaxResendsPerHour; i++)
        {
            principal.IssueCode(CodePurpose.EmailVerify, $"c{i}", _clock).IsSuccess.ShouldBeTrue();
        }

        principal.IssueCode(CodePurpose.EmailVerify, "late", _clock).Errors.ShouldBe([IdentityErrors.ResendLimitReached]);

        _clock.Advance(TimeSpan.FromHours(1));
        principal.IssueCode(CodePurpose.EmailVerify, "fresh", _clock).Value.ResendCount.ShouldBe(0);
    }

    [Fact]
    public void OpenSession_Active_OpensAndRaisesSessionOpened()
    {
        var principal = ActiveOwner();

        var session = principal.OpenSession("t1", SessionSurface.Console, _clock).Value;

        session.Id.Version.ShouldBe(7);
        session.TokenHash.ShouldBe("t1");
        session.Surface.ShouldBe(SessionSurface.Console);
        session.LastSeenAt.ShouldBe(_clock.Now);
        session.IsLive(_clock).ShouldBeTrue();
        principal.Sessions.ShouldBe([session]);
        Events(principal).ShouldBe([new SessionOpened(principal.Id, session.Id, SessionSurface.Console)]);
    }

    [Fact]
    public void OpenSession_Pending_ReportsAccountPending() =>
        Register().OpenSession("t1", SessionSurface.Api, _clock).Errors.ShouldBe([IdentityErrors.AccountPending]);

    [Fact]
    public void OpenSession_Suspended_ReportsAccountSuspended()
    {
        var principal = ActiveOwner();
        principal.Suspend();

        principal.OpenSession("t1", SessionSurface.Api, _clock).Errors.ShouldBe([IdentityErrors.AccountSuspended]);
    }

    [Fact]
    public void Session_IdleForTwelveHours_IsNoLongerLive()
    {
        var session = ActiveOwner().OpenSession("t1", SessionSurface.Api, _clock).Value;

        _clock.Advance(Session.IdleTimeout);

        session.IsLive(_clock).ShouldBeFalse();
    }

    [Fact]
    public void TouchSession_MovesIdleExpiryButNeverTheAbsoluteOne()
    {
        var principal = ActiveOwner();
        var session = principal.OpenSession("t1", SessionSurface.Api, _clock).Value;
        var absolute = session.AbsoluteExpiresAt;

        for (var elapsed = TimeSpan.Zero; elapsed < Session.AbsoluteLifetime; elapsed += TimeSpan.FromHours(11))
        {
            principal.TouchSession(session.Id, _clock).IsSuccess.ShouldBeTrue();
            session.LastSeenAt.ShouldBe(_clock.Now);
            _clock.Advance(TimeSpan.FromHours(11));
        }

        session.AbsoluteExpiresAt.ShouldBe(absolute);
        principal.TouchSession(session.Id, _clock).Errors.ShouldBe([IdentityErrors.SessionExpired]);
    }

    [Fact]
    public void TouchSession_Unknown_ReportsSessionNotFound() =>
        ActiveOwner().TouchSession(Guid.CreateVersion7(), _clock).Errors.ShouldBe([IdentityErrors.SessionNotFound]);

    [Fact]
    public void RevokeSession_RemovesItAndRaisesSessionRevoked()
    {
        var principal = ActiveOwner();
        var session = principal.OpenSession("t1", SessionSurface.Api, _clock).Value;
        Events(principal);

        principal.RevokeSession(session.Id).IsSuccess.ShouldBeTrue();

        principal.Sessions.ShouldBeEmpty();
        Events(principal).ShouldBe([new SessionRevoked(principal.Id, session.Id)]);
        principal.RevokeSession(session.Id).Errors.ShouldBe([IdentityErrors.SessionNotFound]);
    }

    [Fact]
    public void ChangePassword_KeepsCurrentSessionAndRevokesTheRest()
    {
        var principal = ActiveOwner();
        var current = principal.OpenSession("t1", SessionSurface.Console, _clock).Value;
        var other = principal.OpenSession("t2", SessionSurface.Api, _clock).Value;
        Events(principal);

        principal.ChangePassword("new-hash", current.Id);

        principal.Owner.PasswordHash.ShouldBe("new-hash");
        principal.Sessions.ShouldBe([current]);
        Events(principal).ShouldBe([new SessionRevoked(principal.Id, other.Id), new OwnerPasswordChanged(principal.Id)]);
    }

    [Fact]
    public void ResetPassword_WithCode_ChangesHashAndRevokesEverySession()
    {
        var principal = ActiveOwner();
        var session = principal.OpenSession("t1", SessionSurface.Api, _clock).Value;
        principal.IssueCode(CodePurpose.PasswordReset, "r1", _clock);
        Events(principal);

        principal.ResetPassword("r1", "new-hash", _clock).IsSuccess.ShouldBeTrue();

        principal.Owner.PasswordHash.ShouldBe("new-hash");
        principal.Sessions.ShouldBeEmpty();
        Events(principal).ShouldBe([new SessionRevoked(principal.Id, session.Id), new OwnerPasswordChanged(principal.Id)]);
    }

    [Fact]
    public void RequestEmailChange_KeepsOldEmailUntilConfirmed()
    {
        var principal = ActiveOwner();
        var newEmail = Email.Create("new@example.com").Value;

        var code = principal.RequestEmailChange(newEmail, "e1", _clock).Value;

        code.Purpose.ShouldBe(CodePurpose.EmailChange);
        principal.Owner.Email.Value.ShouldBe("ana@example.com");
        principal.Owner.PendingEmail.ShouldBe(newEmail);
        Events(principal).ShouldBe([new OwnerEmailChangeRequested(principal.Id, "new@example.com")]);
    }

    [Fact]
    public void ConfirmEmailChange_WithCode_SwapsEmailAndRaisesConfirmed()
    {
        var principal = ActiveOwner();
        var newEmail = Email.Create("new@example.com").Value;
        principal.RequestEmailChange(newEmail, "e1", _clock);
        Events(principal);

        principal.ConfirmEmailChange("e1", _clock).IsSuccess.ShouldBeTrue();

        principal.Owner.Email.ShouldBe(newEmail);
        principal.Owner.PendingEmail.ShouldBeNull();
        Events(principal).ShouldBe([new OwnerEmailChangeConfirmed(principal.Id, "ana@example.com", "new@example.com")]);
    }

    [Fact]
    public void ConfirmEmailChange_WithoutRequest_ReportsNoPendingEmailChange()
    {
        var principal = ActiveOwner();
        principal.IssueCode(CodePurpose.EmailChange, "e1", _clock);

        principal.ConfirmEmailChange("e1", _clock).Errors.ShouldBe([IdentityErrors.NoPendingEmailChange]);
    }

    private Principal Register() =>
        Principal.RegisterOwner(PersonName.Create("Ana").Value, Email.Create("ana@example.com").Value, "pw-hash", _clock);

    private Principal RegisterWithCode(CodePurpose purpose, string codeHash)
    {
        var principal = Register();
        principal.IssueCode(purpose, codeHash, _clock);
        Events(principal);
        return principal;
    }

    private Principal ActiveOwner()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "v1");
        principal.VerifyEmail("v1", _clock);
        Events(principal);
        return principal;
    }

    private static IReadOnlyList<IDomainEvent> Events(IHasDomainEvents aggregate) => aggregate.DequeueDomainEvents();
}
