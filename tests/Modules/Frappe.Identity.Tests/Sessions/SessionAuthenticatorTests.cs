using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Frappe.Identity.Tests.Sessions;

/// <summary>Authentication reads a lean credential and loads the aggregate only to touch; a lost touch never throws.</summary>
public sealed class SessionAuthenticatorTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakePrincipals _principals = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFreshSession_IsAdmitted_WithoutLoadingTheAggregate()
    {
        var (principal, session) = _principals.Seed(_clock);

        var authenticated = await Authenticator().AuthenticateAsync("token", Ct);

        authenticated.ShouldBe(new AuthenticatedSession(principal.Id, session.Id, PrincipalType.Owner));
        _principals.AggregateLoads.ShouldBe(0);
        _principals.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task ASessionDueATouch_IsTouchedAndSaved()
    {
        var (principal, session) = _principals.Seed(_clock);
        _clock.Advance(SessionAuthenticator.TouchInterval);

        var authenticated = await Authenticator().AuthenticateAsync("token", Ct);

        authenticated.ShouldNotBeNull().SessionId.ShouldBe(session.Id);
        _principals.AggregateLoads.ShouldBe(1);
        _principals.Saves.ShouldBe(1);
        principal.Sessions.Single().LastSeenAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public async Task ATouchRacingARevocation_IsUnauthenticated_InsteadOfThrowing()
    {
        _principals.Seed(_clock);
        _clock.Advance(SessionAuthenticator.TouchInterval);
        _principals.RevokeOnSave = true;

        var authenticated = await Authenticator().AuthenticateAsync("token", Ct);

        authenticated.ShouldBeNull();
        _principals.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task ATouchLosingARaceToAnUnrelatedChange_StillAdmitsTheLiveSession()
    {
        var (_, session) = _principals.Seed(_clock);
        _clock.Advance(SessionAuthenticator.TouchInterval);
        _principals.ConflictOnSave = true;

        var authenticated = await Authenticator().AuthenticateAsync("token", Ct);

        authenticated.ShouldNotBeNull().SessionId.ShouldBe(session.Id);
    }

    private SessionAuthenticator Authenticator() => new(_principals, new PlainHasher(), _clock);

    private sealed class PlainHasher : ISecretHasher
    {
        public string Hash(string secret) => $"h:{secret}";
    }

    /// <summary>Just enough repository: one principal, a credential read, and a save that can lose a race.</summary>
    private sealed class FakePrincipals : IPrincipalRepository
    {
        private Principal? _principal;
        private bool _revoked;

        public int AggregateLoads { get; private set; }
        public int Saves { get; private set; }
        public bool RevokeOnSave { get; set; }
        public bool ConflictOnSave { get; set; }

        public (Principal Principal, Session Session) Seed(TimeProvider clock)
        {
            var principal = Principal.RegisterOwner(
                PersonName.Create("Ana").Value, Email.Create("ana@example.com").Value, "pw", Locale.Parse("en"), clock);
            principal.IssueCode(CodePurpose.EmailVerify, "c", clock);
            principal.VerifyEmail("c", clock);
            var session = principal.OpenSession("h:token", SessionSurface.Api, clock).Value;
            _principal = principal;
            return (principal, session);
        }

        public Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken)
        {
            var session = _revoked ? null : _principal?.Sessions.SingleOrDefault(s => s.TokenHash == tokenHash);
            return Task.FromResult(session is null
                ? null
                : new SessionCredential(_principal!.Id, _principal.Type, _principal.Owner.Status, session.Id, session.CreatedAt, session.LastSeenAt));
        }

        public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            AggregateLoads++;
            return Task.FromResult(_principal?.Id == id ? _principal : null);
        }

        public Task<bool> SaveUnlessChangedAsync(CancellationToken cancellationToken)
        {
            Saves++;
            _revoked = RevokeOnSave;
            return Task.FromResult(!RevokeOnSave && !ConflictOnSave);
        }

        public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Principal>> ListPendingRegisteredBeforeAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public void Add(Principal principal) => throw new NotSupportedException();
        public void Remove(Principal principal) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> SaveClaimingEmailAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
