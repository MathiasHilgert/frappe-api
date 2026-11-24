using System.Text.Json;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Credentials.ChangePassword;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Application.Owners.ConfirmEmailChange;
using Frappe.Identity.Application.Owners.GetMe;
using Frappe.Identity.Application.Owners.LogInOwner;
using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Owners.RequestEmailChange;
using Frappe.Identity.Application.Owners.RequestPasswordReset;
using Frappe.Identity.Application.Owners.ResendOwnerVerification;
using Frappe.Identity.Application.Owners.ResetPassword;
using Frappe.Identity.Application.Owners.UndoEmailChange;
using Frappe.Identity.Application.Owners.UpdateMe;
using Frappe.Identity.Application.Owners.VerifyOwnerEmail;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Application.Sessions.GetCurrentSession;
using Frappe.Identity.Application.Sessions.StepUp;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Identity.Infrastructure.Owners;
using Frappe.Identity.Infrastructure.Persistence;
using Frappe.Identity.Infrastructure.Sessions;
using Frappe.Observability;
using Frappe.Persistence;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Identity.IntegrationTests;

/// <summary>The Identity module's real use cases, repositories and unit of work over a migrated database, with the outside world faked.</summary>
internal sealed class OwnersHost : IAsyncDisposable
{
    public const string Password = "correct horse battery";

    public static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    private OwnersHost(string owner, ServiceProvider provider, FlooredTime time, CountingLimiter limiter, ListedPasswords breached)
    {
        Breached = breached;
        OwnerConnection = owner;
        _provider = provider;
        Time = time;
        Limiter = limiter;
    }

    public string OwnerConnection { get; }

    public FlooredTime Time { get; }

    public CountingLimiter Limiter { get; }

    /// <summary>The passwords the breach list knows; none to start with.</summary>
    public HashSet<string> Breached { get; }

    /// <param name="owner">The owner connection string of the migrated database.</param>
    /// <param name="gate">When given, every address lookup waits there after reading, so racing registrations all read the same state.</param>
    /// <param name="saving">When given, every save waits there before writing, so racing commands all read before any of them commits.</param>
    /// <param name="beforeSaving">When given, runs before every save writes, as another request that commits first.</param>
    public static OwnersHost Over(string owner, RaceGate? gate = null, RaceGate? saving = null, Func<Task>? beforeSaving = null)
    {
        var time = new FlooredTime(Start);
        var limiter = new CountingLimiter();
        var breached = new ListedPasswords();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = owner,
            ["Identity:Passwords:MemoryMebibytes"] = "8",
            ["Identity:Passwords:Iterations"] = "1",
            ["Identity:Codes:Key"] = "bG9jYWwtb25seS1jb2RlLWhhc2gta2V5LW9mLTM2LWJ5dGVzIQ==",
        }).Build());
        services.AddSingleton<TimeProvider>(time);
        new IdentityModule().AddServices(services);
        services.AddCodes();
        services.AddSessions();
        services.AddOwners();
        services.RemoveAll<IBreachedPasswords>();
        services.AddSingleton<IBreachedPasswords>(breached);
        services.AddSingleton<IAttemptLimiter>(limiter);
        services.AddHandlers(typeof(IEmailIdentifiers).Assembly);
        if (saving is not null)
        {
            services.ConfigureDbContext<IdentityDbContext>(options => options.AddInterceptors(new GatedSaves(saving)));
        }

        if (beforeSaving is not null)
        {
            services.ConfigureDbContext<IdentityDbContext>(options => options.AddInterceptors(new BeforeSaves(beforeSaving)));
        }

        if (gate is not null)
        {
            services.AddScoped<IEmailIdentifiers>(provider => new GatedEmailIdentifiers(
                new EfEmailIdentifiers(provider.GetRequiredService<IdentityDbContext>()), gate));
        }

        return new OwnersHost(owner, services.BuildServiceProvider(), time, limiter, breached);
    }

    public async Task<Result<RegisteredOwner>> RegisterAsync(
        string email = "ana@example.com", string given = "Ana", string? family = "Pérez", string password = Password, string locale = "es-AR")
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterOwner, RegisteredOwner>>()
            .HandleAsync(new RegisterOwner(given, family, email, password, locale), default);
    }

    public async Task<Result<OpenedSession>> VerifyAsync(string? ownerId, string? code)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<VerifyOwnerEmail, OpenedSession>>()
            .HandleAsync(new VerifyOwnerEmail(ownerId, code, "agent", System.Net.IPAddress.Parse("203.0.113.7")), default);
    }

    public async Task<Result<Unit>> ResendAsync(string? ownerId)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ResendOwnerVerification, Unit>>()
            .HandleAsync(new ResendOwnerVerification(ownerId), default);
    }

    /// <summary>
    /// Logs in. A refusal waits for its floor on the fake clock, which moves on by the wait (see <see cref="FlooredTime"/>).
    /// </summary>
    public async Task<Result<LogInOutcome>> LogInAsync(string? email, string? password, string client = "203.0.113.7")
    {
        return await StartLogInAsync(email, password, client);
    }

    /// <summary>Starts a log-in and leaves it running: one that succeeds is complete at once, a refusal waits for the clock.</summary>
    public async Task<Result<LogInOutcome>> StartLogInAsync(string? email, string? password, string client = "203.0.113.7")
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<LogInOwner, LogInOutcome>>()
            .HandleAsync(new LogInOwner(email, password, "agent", System.Net.IPAddress.Parse(client)), default);
    }

    public async Task<Result<OwnerProfile>> MeAsync(Guid principalId)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetMe, OwnerProfile>>().HandleAsync(new GetMe(principalId), default);
    }

    public async Task<Result<Unit>> UpdateMeAsync(UpdateMe command)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateMe, Unit>>().HandleAsync(command, default);
    }

    /// <summary>Changes a password. A wrong current one waits for its floor on the fake clock.</summary>
    public async Task<Result<Unit>> ChangePasswordAsync(Guid principalId, Guid sessionId, string? current, string? replacement) =>
        await SettleAsync(async () =>
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangePassword, Unit>>()
                .HandleAsync(new ChangePassword(principalId, sessionId, current, replacement), default);
        });

    /// <summary>Steps a session up. A wrong password waits for its floor on the fake clock.</summary>
    public async Task<Result<SteppedUp>> StepUpAsync(Guid principalId, Guid sessionId, string? password) =>
        await SettleAsync(async () =>
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<StepUp, SteppedUp>>()
                .HandleAsync(new StepUp(principalId, sessionId, password), default);
        });

    /// <summary>The session as its holder sees it now.</summary>
    public async Task<Result<CurrentSessionView>> CurrentAsync(Guid sessionId)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCurrentSession, CurrentSessionView>>()
            .HandleAsync(new GetCurrentSession(sessionId), default);
    }

    public async Task<Result<PasswordResetRequested>> RequestResetAsync(string? email)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestPasswordReset, PasswordResetRequested>>()
            .HandleAsync(new RequestPasswordReset(email), default);
    }

    public async Task<Result<Unit>> ResetPasswordAsync(
        string? resetId, string? code, string? replacement, string client = "203.0.113.7")
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetPassword, Unit>>()
            .HandleAsync(new ResetPassword(resetId, code, replacement, System.Net.IPAddress.Parse(client)), default);
    }

    public async Task<Result<EmailChangeRequested>> RequestEmailChangeAsync(Guid principalId, string? newEmail)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestEmailChange, EmailChangeRequested>>()
            .HandleAsync(new RequestEmailChange(principalId, newEmail), default);
    }

    public async Task<Result<Unit>> ConfirmEmailChangeAsync(
        Guid principalId, Guid sessionId, string? changeId, string? code, string client = "203.0.113.7")
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ConfirmEmailChange, Unit>>()
            .HandleAsync(new ConfirmEmailChange(principalId, sessionId, changeId, code, System.Net.IPAddress.Parse(client)), default);
    }

    public async Task<Result<Unit>> UndoEmailChangeAsync(string? token)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<UndoEmailChange, Unit>>()
            .HandleAsync(new UndoEmailChange(token), default);
    }

    /// <summary>Whether the token of <paramref name="session"/> still lets its holder in, as the authentication of every request decides.</summary>
    public async Task<bool> LetsInAsync(OpenedSession session)
    {
        await using var scope = _provider.CreateAsyncScope();
        var hash = scope.ServiceProvider.GetRequiredService<ISessionTokens>().HashOf(session.Token.Value);
        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<AuthenticateSession, SessionPrincipal>>()
            .HandleAsync(new AuthenticateSession(hash!), default);
        return result.IsSuccess;
    }

    /// <summary>Runs a command that may wait for its floor on the fake clock.</summary>
    private static async Task<T> SettleAsync<T>(Func<Task<T>> command)
    {
        return await command();
    }

    public IdentityDbContext Context() =>
        new(ModuleDatabase.Options<IdentityDbContext>(OwnerConnection, IdentityDbContext.Schema));

    /// <summary>The integration events in the outbox, oldest first: their type and data.</summary>
    public async Task<List<(string Type, JsonElement Data)>> OutboxAsync()
    {
        await using var db = Context();
        var rows = await db.Database
            .SqlQuery<OutboxRow>($"SELECT type, data::text AS data FROM identity.outbox_messages ORDER BY sequence").ToListAsync();
        return [.. rows.Select(row => (row.Type, JsonElement.Parse(row.Data)))];
    }

    /// <summary>The code of the last <c>owner_code_issued</c> event: what the mail carries.</summary>
    public async Task<string> LastCodeAsync() =>
        (await OutboxAsync()).Last(e => e.Type == "frappe.identity.owner_code_issued.v1").Data.GetProperty("code").GetString()!;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>One outbox row as read back: its type and its data as JSON text.</summary>
    internal sealed record OutboxRow(string Type, string Data);

    /// <summary>A breach list that knows only the passwords a test lists, so the database tests never reach the network.</summary>
    private sealed class ListedPasswords : HashSet<string>, IBreachedPasswords
    {
        public Task<bool?> IsBreachedAsync(Password password, CancellationToken cancellationToken) =>
            Task.FromResult<bool?>(Contains(password.Value));
    }

    /// <summary>Lets racing registrations read before any of them writes: each lookup waits for the others.</summary>
    internal sealed class RaceGate(int parties)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == parties)
            {
                _open.SetResult();
            }

            return _open.Task;
        }
    }

    /// <summary>Holds every save at <see cref="RaceGate"/> before it writes.</summary>
    private sealed class GatedSaves(RaceGate gate) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await gate.ArriveAsync();
            return result;
        }
    }

    /// <summary>Runs <paramref name="action"/> (another request committing) before every save writes.</summary>
    private sealed class BeforeSaves(Func<Task> action) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await action();
            return result;
        }
    }

    /// <summary>The real address repository, held at <see cref="RaceGate"/> after each lookup.</summary>
    private sealed class GatedEmailIdentifiers(EfEmailIdentifiers inner, RaceGate gate) : IEmailIdentifiers
    {
        public async Task<Domain.Identifiers.EmailIdentifier?> FindByAddressAsync(string normalizedAddress, CancellationToken cancellationToken)
        {
            var found = await inner.FindByAddressAsync(normalizedAddress, cancellationToken);
            await gate.ArriveAsync();
            return found;
        }

        public Task<Domain.Identifiers.EmailIdentifier?> FindOfAsync(Guid principalId, CancellationToken cancellationToken) =>
            inner.FindOfAsync(principalId, cancellationToken);

        public void Add(Domain.Identifiers.EmailIdentifier identifier) => inner.Add(identifier);
    }

    /// <summary>
    /// A fake clock whose waits end at once: a delay moves the clock on by its length, so a refusal held to its floor takes no real time
    /// and the clock still shows that the floor passed.
    /// </summary>
    internal sealed class FlooredTime(DateTimeOffset start) : FakeTimeProvider(start)
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan && period == Timeout.InfiniteTimeSpan)
            {
                _ = Task.Run(() => Advance(dueTime));
            }

            return timer;
        }
    }

    /// <summary>A limiter that counts like the real one in memory and can be told to be down.</summary>
    internal sealed class CountingLimiter : IAttemptLimiter
    {
        private readonly Dictionary<(string Flow, string Subject), int> _hits = [];

        public bool Down { get; set; }

        /// <summary>How many attempts of <paramref name="flow"/> were counted for <paramref name="subject"/>.</summary>
        public int HitsOf(string flow, string subject)
        {
            lock (_hits)
            {
                return _hits.GetValueOrDefault((flow, subject));
            }
        }

        /// <summary>How many attempts of <paramref name="flow"/> were counted for every subject together.</summary>
        public int HitsOfFlow(string flow)
        {
            lock (_hits)
            {
                return _hits.Where(hit => hit.Key.Flow == flow).Sum(hit => hit.Value);
            }
        }

        public Task<AttemptVerdict> HitAsync(LimitFlow flow, string subjectKey, CancellationToken cancellationToken)
        {
            if (Down)
            {
                return Task.FromResult(AttemptVerdict.Unavailable);
            }

            lock (_hits)
            {
                var key = (flow.Name, subjectKey);
                _hits[key] = _hits.GetValueOrDefault(key) + 1;
                return Task.FromResult(_hits[key] > flow.Limit ? AttemptVerdict.Refused(flow.Window) : AttemptVerdict.Allowed);
            }
        }
    }
}
