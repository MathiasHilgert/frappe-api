using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Principals.Purge;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Frappe.Identity.Tests.Principals;

/// <summary>The purge pages past rows it cannot purge and saves each principal on its own.</summary>
public sealed class PurgePendingOwnersHandlerTests
{
    private static readonly DateTimeOffset LongAgo = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(LongAgo.AddDays(30));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AConflict_SkipsOnlyThatPrincipal_AndIsCounted()
    {
        var principals = new PagedPrincipals(count: 3, conflictOn: 1);
        var telemetry = new RecordingTelemetry();

        var events = await Handle(principals, telemetry);

        principals.Removed.ShouldBe([principals.Ids[0], principals.Ids[2]]);
        events.OfType<OwnerPendingPurged>().Select(e => e.PrincipalId).ShouldBe([principals.Ids[0], principals.Ids[2]]);
        telemetry.SkippedConflicts.ShouldBe(1);
        telemetry.Purged.ShouldBe(2);
    }

    [Fact]
    public async Task Paging_MovesPastRowsItCouldNotPurge_InsteadOfReReadingTheSameOldestPage()
    {
        var principals = new PagedPrincipals(count: PurgePendingOwnersHandler.PageSize + 5, conflictOn: 0);
        var telemetry = new RecordingTelemetry();

        await Handle(principals, telemetry);

        principals.Removed.Count.ShouldBe(PurgePendingOwnersHandler.PageSize + 4);
        principals.Removed.ShouldNotContain(principals.Ids[0]);
        principals.Cursors.ShouldBe([null, principals.KeyOf(PurgePendingOwnersHandler.PageSize - 1)]);
        telemetry.Purged.ShouldBe(PurgePendingOwnersHandler.PageSize + 4);
    }

    [Fact]
    public async Task ARun_StopsAtItsBudget_AndLeavesTheRestForTheNextRun()
    {
        var principals = new PagedPrincipals(count: PurgePendingOwnersHandler.BatchSize + 10, conflictOn: -1);

        await Handle(principals, new RecordingTelemetry());

        principals.Removed.Count.ShouldBe(PurgePendingOwnersHandler.BatchSize);
    }

    private Task<Wolverine.OutgoingMessages> Handle(PagedPrincipals principals, RecordingTelemetry telemetry) =>
        PurgePendingOwnersHandler.Handle(new PurgePendingOwners(), principals, telemetry, _clock,
            NullLogger<PurgePendingOwners>.Instance, Ct);

    private sealed class RecordingTelemetry : IIdentityTelemetry
    {
        public int SkippedConflicts { get; private set; }
        public int Purged { get; private set; }

        public void LoginThrottled() { }
        public void CodeRejected(CodePurpose purpose) { }
        public void PurgeSkippedConflict() => SkippedConflicts++;
        public void PurgedOwners(int count) => Purged += count;
    }

    private sealed class PagedPrincipals : IPrincipalRepository
    {
        private readonly Dictionary<Guid, Principal> _byId;
        private readonly int _conflictOn;
        private readonly List<PurgeCandidate> _candidates;
        private Guid? _pending;
        private int _saves;

        public PagedPrincipals(int count, int conflictOn)
        {
            _conflictOn = conflictOn;
            var all = Enumerable.Range(0, count).Select(i => Principal.RegisterOwner(
                PersonName.Create("Ana").Value, Email.Create($"ana{i}@example.com").Value, "pw", Locale.Parse("en"),
                LongAgo.AddSeconds(i))).ToList();
            _byId = all.ToDictionary(p => p.Id);
            _candidates = [.. all.Select(p => new PurgeCandidate(p.Id, p.RegisteredAt))];
            Ids = [.. all.Select(p => p.Id)];
        }

        public IReadOnlyList<Guid> Ids { get; }
        public List<Guid> Removed { get; } = [];
        public List<PurgeCandidate?> Cursors { get; } = [];

        public PurgeCandidate KeyOf(int index) => _candidates[index];

        public Task<IReadOnlyList<PurgeCandidate>> ListPurgeCandidatesAsync(
            DateTimeOffset cutoff, PurgeCandidate? after, int limit, CancellationToken cancellationToken)
        {
            Cursors.Add(after);
            IReadOnlyList<PurgeCandidate> page = [.. _candidates
                .Where(c => c.RegisteredAt < cutoff && (after is null || (c.RegisteredAt, c.Id).CompareTo((after.RegisteredAt, after.Id)) > 0))
                .Take(limit)];
            return Task.FromResult(page);
        }

        public Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Principal?>(_byId.GetValueOrDefault(id));

        public void Remove(Principal principal) => _pending = principal.Id;

        public Task<Result> SaveAsync(CancellationToken cancellationToken)
        {
            var conflict = _saves++ == _conflictOn;
            if (!conflict && _pending is { } id)
                Removed.Add(id);
            _pending = null;
            return Task.FromResult(conflict ? Result.Failure(IdentityErrors.ConcurrentChange) : Result.Success());
        }

        public Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Add(Principal principal) => throw new NotSupportedException();
    }
}
