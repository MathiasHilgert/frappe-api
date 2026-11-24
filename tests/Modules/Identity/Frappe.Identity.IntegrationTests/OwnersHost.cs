using System.Text.Json;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Application.Owners.RegisterOwner;
using Frappe.Identity.Application.Owners.ResendOwnerVerification;
using Frappe.Identity.Application.Owners.VerifyOwnerEmail;
using Frappe.Identity.Application.Sessions;
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

    private OwnersHost(string owner, ServiceProvider provider, FakeTimeProvider time, CountingLimiter limiter)
    {
        OwnerConnection = owner;
        _provider = provider;
        Time = time;
        Limiter = limiter;
    }

    public string OwnerConnection { get; }

    public FakeTimeProvider Time { get; }

    public CountingLimiter Limiter { get; }

    /// <param name="owner">The owner connection string of the migrated database.</param>
    /// <param name="gate">When given, every address lookup waits there after reading, so racing registrations all read the same state.</param>
    public static OwnersHost Over(string owner, RaceGate? gate = null)
    {
        var time = new FakeTimeProvider(Start);
        var limiter = new CountingLimiter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Identity"] = owner,
            ["Identity:Codes:Key"] = "bG9jYWwtb25seS1jb2RlLWhhc2gta2V5LW9mLTM2LWJ5dGVzIQ==",
        }).Build());
        services.AddSingleton<TimeProvider>(time);
        new IdentityModule().AddServices(services);
        services.AddCodes();
        services.AddSessions();
        services.AddOwners();
        services.RemoveAll<IBreachedPasswords>();
        services.AddSingleton<IBreachedPasswords, CleanPasswords>();
        services.AddSingleton<IAttemptLimiter>(limiter);
        services.AddHandlers(typeof(IEmailIdentifiers).Assembly);
        if (gate is not null)
        {
            services.AddScoped<IEmailIdentifiers>(provider => new GatedEmailIdentifiers(
                new EfEmailIdentifiers(provider.GetRequiredService<IdentityDbContext>()), gate));
        }

        return new OwnersHost(owner, services.BuildServiceProvider(), time, limiter);
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

    /// <summary>A breach list that knows no password, so the database tests never reach the network.</summary>
    private sealed class CleanPasswords : IBreachedPasswords
    {
        public Task<bool?> IsBreachedAsync(Password password, CancellationToken cancellationToken) => Task.FromResult<bool?>(false);
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

    /// <summary>A limiter that counts like the real one in memory and can be told to be down.</summary>
    internal sealed class CountingLimiter : IAttemptLimiter
    {
        private readonly Dictionary<(string Flow, string Subject), int> _hits = [];

        public bool Down { get; set; }

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
