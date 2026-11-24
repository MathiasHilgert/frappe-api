using System.Collections.Concurrent;
using System.Net;
using Frappe.Access.Infrastructure;
using Frappe.Http;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Http;
using Frappe.Identity.Http.Sessions;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Modules;
using Frappe.Observability;
using Frappe.Persistence;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The real Api with Identity's real session handlers, authentication scheme, cookie and tokens, over the sessions of an in-memory store and a
/// unit of work that only counts: "seeded principal and session" without a database (the database side is tested in the module's own suite).
/// </summary>
internal sealed class IdentitySessionHost : IAsyncDisposable
{
    public const string ConsoleOrigin = "https://console.example.com";

    private static readonly Dictionary<string, string?> Settings = new() { ["Http:Console:Origins"] = ConsoleOrigin };

    private IdentitySessionHost(WebApplication app, SessionStore store, CountingUnitOfWork unitOfWork, SettableClock time)
    {
        App = app;
        Store = store;
        UnitOfWork = unitOfWork;
        Time = time;
    }

    public static Instant Start { get; } = Instant.FromUtc(2026, 11, 24, 12, 0);

    public WebApplication App { get; }

    public SessionStore Store { get; }

    public CountingUnitOfWork UnitOfWork { get; }

    public SettableClock Time { get; }

    public static async Task<IdentitySessionHost> StartAsync(ILoggerProvider? logs = null, IModule? extra = null)
    {
        var store = new SessionStore();
        var unitOfWork = new CountingUnitOfWork();
        var time = new SettableClock(Start.ToDateTimeOffset());
        IModule[] modules = extra is null
            ? [new AccessModule(), new SessionModule(store, unitOfWork, time)]
            : [new AccessModule(), new SessionModule(store, unitOfWork, time), extra];
        var app = await ApiHost.StartAsync(modules, [new IdentityHttp(), new ProbeHttp()], Settings, logs: logs);
        return new IdentitySessionHost(app, store, unitOfWork, time);
    }

    /// <summary>Seeds an active principal with a live session; the token is what a client sends.</summary>
    public (Principal Principal, Session Session, string Token) Seed(PrincipalStatus status = PrincipalStatus.Active, bool staleStamp = false)
    {
        var tokens = new SessionTokens();
        var issued = tokens.Issue();
        var principal = Principal.Create(PrincipalKind.Owner, status, Start);
        var session = Session.Open(
            principal.Id, issued.Hash, SessionAudience.Console, staleStamp ? Guid.CreateVersion7() : principal.CredentialStamp, Start,
            Duration.FromHours(12), Duration.FromDays(30), "agent", null);
        Store.Principals.Add(principal);
        Store.Sessions.Add(session);
        return (principal, session, issued.Value);
    }

    /// <summary>
    /// Seeds one more session of <paramref name="principal"/>, opened at <paramref name="openedAt"/>;
    /// the token is what a client sends.
    /// </summary>
    public (Session Session, string Token) SeedSessionOf(Principal principal, Instant openedAt, string? userAgent = null, IPNetwork? ipPrefix = null)
    {
        var issued = new SessionTokens().Issue();
        var session = Session.Open(
            principal.Id, issued.Hash, SessionAudience.Console, principal.CredentialStamp, openedAt, Duration.FromHours(12), Duration.FromDays(30),
            userAgent, ipPrefix);
        Store.Sessions.Add(session);
        return (session, issued.Value);
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();

    /// <summary>
    /// A clock the test sets: only what the current time is. Its timers stay real, because the host's own pieces (the rate limiter's store
    /// timeouts) wait on them and a fake timer would never fire.
    /// </summary>
    internal sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        /// <summary>When set, every timestamp is this one, so no time seems to pass between two of them.</summary>
        public long? FrozenTimestamp { get; set; }

        /// <summary>The due times of every timer created, in order.</summary>
        public ConcurrentQueue<TimeSpan> Waits { get; } = new();

        public override long GetTimestamp() => FrozenTimestamp ?? base.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waits.Enqueue(dueTime);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>The ports of Identity's sessions over memory, the real handlers behind them.</summary>
    private sealed class SessionModule(SessionStore store, CountingUnitOfWork unitOfWork, SettableClock time) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddMetrics();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
            services.TryAddSingleton<IdentityMetrics>();
            services.AddSingleton(new SessionLifetimes(Duration.FromHours(12), Duration.FromDays(30)));
            services.AddSingleton<ISessionTokens, SessionTokens>();
            services.AddSingleton<ISessions>(store);
            services.AddSingleton<ISessionReads>(store);
            services.AddKeyedSingleton<IUnitOfWork>("identity", unitOfWork);
            services.AddHandlers(typeof(ISessions).Assembly);
        }
    }

    /// <summary>A route nobody guards and one that needs a session, to prove that only the second reads one.</summary>
    private sealed class ProbeHttp : IHttpModule
    {
        public void AddServices(IServiceCollection services)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder v1)
        {
            v1.MapGet("/probe/open", () => "open").WithName("ProbeOpen");
            v1.MapPost("/probe/open", () => "open").WithName("ProbeOpenPost");
            v1.MapGet("/probe/step-up", () => "stepped up").WithName("ProbeStepUp").RequireSession().RequireStepUp();
        }
    }

    /// <summary>Counts what a command commits and can lose the race a real commit may lose.</summary>
    internal sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public int Rollbacks { get; private set; }

        public bool LoseTheRace { get; set; }

        public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (LoseTheRace)
            {
                throw new CommitConflictException("Another request changed the same session first.");
            }

            Commits++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            Rollbacks++;
            return Task.CompletedTask;
        }
    }

    /// <summary>The sessions and principals in memory; every lookup by token hash is counted.</summary>
    internal sealed class SessionStore : ISessions, ISessionReads
    {
        public List<Session> Sessions { get; } = [];

        public List<Principal> Principals { get; } = [];

        public int Lookups { get; private set; }

        public Task<Session?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Sessions.SingleOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<Session>> ListLiveOfAsync(Guid principalId, Instant now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Session>>([.. Sessions.Where(s => s.PrincipalId == principalId && s.IsLiveAt(now))]);

        public void Add(Session session) => Sessions.Add(session);

        public Task<IReadOnlyList<ListedSession>> ListLiveAsync(
            Guid principalId, Instant now, int limit, SessionPosition? after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ListedSession>>([.. Sessions
                .Where(s => s.PrincipalId == principalId && s.IsLiveAt(now))
                .OrderByDescending(s => s.OpenedAt).ThenBy(s => s.Id)
                .Where(s => after is null || s.OpenedAt < after.OpenedAt || (s.OpenedAt == after.OpenedAt && s.Id.CompareTo(after.Id) > 0))
                .Take(limit)
                .Select(s => new ListedSession(s.Id, s.OpenedAt, s.LastSeenAt, s.UserAgent, s.IpPrefix))]);

        public Task<SessionAccess?> FindAccessAsync(byte[] tokenHash, CancellationToken cancellationToken)
        {
            Lookups++;
            var session = Sessions.SingleOrDefault(s => s.TokenHash.SequenceEqual(tokenHash));
            var principal = session is null ? null : Principals.Single(p => p.Id == session.PrincipalId);
            return Task.FromResult(session is null || principal is null
                ? null
                : new SessionAccess(
                    session.Id, principal.Id, principal.Kind, principal.Status, principal.CredentialStamp, session.CredentialStamp, session.Methods,
                    session.LastSeenAt, session.IdleExpiresAt, session.AbsoluteExpiresAt, session.EndedAt, session.RevokedAt, session.StepUpUntil));
        }

        public Task<CurrentSession?> FindCurrentAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(s => s.Id == id) is { } session
                ? new CurrentSession(session.Id, session.IdleExpiresAt, session.AbsoluteExpiresAt, session.StepUpUntil)
                : null);
    }
}
