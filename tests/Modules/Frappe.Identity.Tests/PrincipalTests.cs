using Microsoft.Extensions.Time.Testing;
using Frappe.SharedKernel.Localization;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Domain;
using Shouldly;

namespace Frappe.Identity.Tests;

public sealed class PrincipalTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

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
        principal.RegisteredAt.ShouldBe(_clock.GetUtcNow());
        Events(principal).ShouldBe([new OwnerRegistrationSubmitted(principal.Id, "ana@example.com")]);
    }

    [Fact]
    public void VerifyEmail_WithLiveCode_ActivatesAndRaisesEmailVerified()
    {
        var principal = RegisterWithCode(CodePurpose.EmailVerify, "c1");

        var result = principal.VerifyEmail("c1", _clock);

        result.IsSuccess.ShouldBeTrue();
        principal.Owner.Status.ShouldBe(OwnerStatus.Active);
        principal.Codes.Single().UsedAt.ShouldBe(_clock.GetUtcNow());
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
        session.LastSeenAt.ShouldBe(_clock.GetUtcNow());
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
            session.LastSeenAt.ShouldBe(_clock.GetUtcNow());
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
    public void CompleteStepUp_MakesTheSessionFreshForFiveMinutesOnly()
    {
        var principal = ActiveOwner();
        var session = principal.OpenSession("t1", SessionSurface.Console, _clock).Value;
        principal.HasFreshStepUp(session.Id, _clock).ShouldBeFalse();

        var expiresAt = principal.CompleteStepUp(session.Id, _clock);

        expiresAt.Value.ShouldBe(_clock.GetUtcNow() + Session.StepUpValidity);
        principal.HasFreshStepUp(session.Id, _clock).ShouldBeTrue();
        _clock.Advance(Session.StepUpValidity - TimeSpan.FromSeconds(1));
        principal.TouchSession(session.Id, _clock);
        principal.HasFreshStepUp(session.Id, _clock).ShouldBeTrue();
        _clock.Advance(TimeSpan.FromSeconds(1));
        principal.HasFreshStepUp(session.Id, _clock).ShouldBeFalse();
    }

    [Fact]
    public void CompleteStepUp_IsBoundToItsSession()
    {
        var principal = ActiveOwner();
        var stepped = principal.OpenSession("t1", SessionSurface.Console, _clock).Value;
        var other = principal.OpenSession("t2", SessionSurface.Console, _clock).Value;

        principal.CompleteStepUp(stepped.Id, _clock);

        principal.HasFreshStepUp(other.Id, _clock).ShouldBeFalse();
        principal.HasFreshStepUp(Guid.NewGuid(), _clock).ShouldBeFalse();
    }

    [Fact]
    public void CompleteStepUp_UnknownOrExpiredSession_Fails()
    {
        var principal = ActiveOwner();
        var session = principal.OpenSession("t1", SessionSurface.Console, _clock).Value;

        principal.CompleteStepUp(Guid.NewGuid(), _clock).Errors.ShouldBe([IdentityErrors.SessionNotFound]);
        _clock.Advance(Session.IdleTimeout);
        principal.CompleteStepUp(session.Id, _clock).Errors.ShouldBe([IdentityErrors.SessionExpired]);
        principal.HasFreshStepUp(session.Id, _clock).ShouldBeFalse();
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
        var code = principal.RequestEmailChange(newEmail, "e1", _clock).Value;
        Events(principal);

        principal.ConfirmEmailChange(code.Id, "e1", _clock).IsSuccess.ShouldBeTrue();

        principal.Owner.Email.ShouldBe(newEmail);
        principal.Owner.PendingEmail.ShouldBeNull();
        Events(principal).ShouldBe([new OwnerEmailChangeConfirmed(principal.Id, "ana@example.com", "new@example.com")]);
    }

    [Fact]
    public void ConfirmEmailChange_WithoutRequest_ReportsNoPendingEmailChange()
    {
        var principal = ActiveOwner();
        var code = principal.IssueCode(CodePurpose.EmailChange, "e1", _clock).Value;

        principal.ConfirmEmailChange(code.Id, "e1", _clock).Errors.ShouldBe([IdentityErrors.NoPendingEmailChange]);
    }

    [Fact]
    public void ConfirmEmailChange_ForAReplacedRequest_ReportsNotFoundAndKeepsTheEmail()
    {
        var principal = ActiveOwner();
        var first = principal.RequestEmailChange(Email.Create("one@example.com").Value, "e1", _clock).Value;
        principal.RequestEmailChange(Email.Create("two@example.com").Value, "e2", _clock);

        principal.ConfirmEmailChange(first.Id, "e2", _clock).Errors.ShouldBe([IdentityErrors.EmailChangeNotFound]);
        principal.Owner.Email.Value.ShouldBe("ana@example.com");
    }

    [Fact]
    public void PurgeAbandonedRegistration_PendingPastGrace_RaisesPendingPurged()
    {
        var principal = Register();
        Events(principal);
        _clock.Advance(Principal.PendingGracePeriod + TimeSpan.FromSeconds(1));

        principal.PurgeAbandonedRegistration(_clock).IsSuccess.ShouldBeTrue();

        Events(principal).ShouldBe([new OwnerPendingPurged(principal.Id)]);
    }

    [Fact]
    public void PurgeAbandonedRegistration_PendingWithinGrace_FailsAndRaisesNothing()
    {
        var principal = Register();
        Events(principal);
        _clock.Advance(Principal.PendingGracePeriod);

        principal.PurgeAbandonedRegistration(_clock).Errors.ShouldBe([IdentityErrors.RegistrationInGrace]);
        Events(principal).ShouldBeEmpty();
    }

    [Fact]
    public void PurgeAbandonedRegistration_ActiveOrSuspended_FailsAsNotPending()
    {
        var active = ActiveOwner();
        var suspended = ActiveOwner();
        suspended.Suspend();
        _clock.Advance(TimeSpan.FromDays(30));

        active.PurgeAbandonedRegistration(_clock).Errors.ShouldBe([IdentityErrors.AccountNotPending]);
        suspended.PurgeAbandonedRegistration(_clock).Errors.ShouldBe([IdentityErrors.AccountNotPending]);
        Events(active).ShouldBeEmpty();
        Events(suspended).ShouldBeEmpty();
    }

    private Principal Register() =>
        Principal.RegisterOwner(PersonName.Create("Ana").Value, Email.Create("ana@example.com").Value, "pw-hash", Locale.Parse("en"), _clock);

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
