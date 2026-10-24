using System.Reflection;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Shouldly;

namespace Frappe.Identity.Tests.Sessions;

/// <summary>Revoking is idempotent, so one lost concurrency race is retried on fresh state.</summary>
public sealed class RevokeSessionHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneLostRace_IsRetriedOnAFreshLoad_AndSucceeds()
    {
        var principals = new RacingPrincipals(conflicts: 1);

        var result = await RevokeSessionHandler.Handle(new RevokeSession(principals.Id, principals.SessionId), principals, Ct);

        result.IsSuccess.ShouldBeTrue();
        principals.Loads.ShouldBe(2);
        principals.Saves.ShouldBe(2);
    }

    [Fact]
    public async Task ARaceLostTwice_AnswersConcurrentChange()
    {
        var principals = new RacingPrincipals(conflicts: 2);

        var result = await RevokeSessionHandler.Handle(new RevokeSession(principals.Id, principals.SessionId), principals, Ct);

        result.Errors.ShouldBe([IdentityErrors.ConcurrentChange]);
        principals.Saves.ShouldBe(2);
    }

    private sealed class RacingPrincipals(int conflicts) : IPrincipalRepository
    {
        private readonly TimeProvider _clock = TimeProvider.System;
        private int _conflictsLeft = conflicts;

        public Guid Id { get; } = Guid.CreateVersion7();
        public Guid SessionId { get; } = Guid.CreateVersion7();
        public int Loads { get; private set; }
        public int Saves { get; private set; }

        public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            Loads++;
            var principal = Principal.RegisterOwner(
                PersonName.Create("Ana").Value, Email.Create("ana@example.com").Value, "pw", Locale.Parse("en"), _clock);
            principal.IssueCode(CodePurpose.EmailVerify, "c", _clock);
            principal.VerifyEmail("c", _clock);
            var session = principal.OpenSession("h", SessionSurface.Api, _clock).Value;
            // Every load is fresh state holding the same session, as a reload from the database would.
            typeof(Session).GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(session, SessionId);
            return Task.FromResult<Principal?>(principal);
        }

        public Task<Result> SaveAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.FromResult(_conflictsLeft-- > 0 ? Result.Failure(IdentityErrors.ConcurrentChange) : Result.Success());
        }

        public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Principal>> ListPendingRegisteredBeforeAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public void Add(Principal principal) => throw new NotSupportedException();
        public void Remove(Principal principal) => throw new NotSupportedException();
        public Task<Result> SaveClaimingEmailAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SaveUnlessChangedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
