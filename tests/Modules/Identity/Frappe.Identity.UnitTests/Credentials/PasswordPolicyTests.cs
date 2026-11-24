using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>What a new password must be (S4): valid length and not breached; a list that cannot answer lets it through and is counted.</summary>
public sealed class PasswordPolicyTests
{
    private const string Valid = "correct horse battery";

    [Test]
    public async Task A_valid_password_the_list_does_not_hold_is_accepted_and_counted_clean()
    {
        // Arrange
        using var fixture = new Fixture(false);

        // Act
        var result = await fixture.Policy.AcceptAsync(Valid, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Value).IsEqualTo(Valid);
            await Assert.That(fixture.Outcomes).IsEquivalentTo(["clean"]);
        }
    }

    [Test]
    public async Task A_breached_password_is_refused_with_its_code_and_counted_breached()
    {
        // Arrange
        using var fixture = new Fixture(true);

        // Act
        var result = await fixture.Policy.AcceptAsync(Valid, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single()).IsTypeOf<RuleViolationError>();
            await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordBreached);
            await Assert.That(fixture.Outcomes).IsEquivalentTo(["breached"]);
        }
    }

    [Test]
    public async Task A_list_that_cannot_answer_lets_the_password_through_and_counts_unavailable()
    {
        // Arrange
        using var fixture = new Fixture(null);

        // Act
        var result = await fixture.Policy.AcceptAsync(Valid, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(fixture.Outcomes).IsEquivalentTo(["unavailable"]);
        }
    }

    [Test]
    [Arguments("short")]
    [Arguments(null)]
    public async Task A_password_with_a_wrong_length_is_refused_without_asking_the_list(string? raw)
    {
        // Arrange
        using var fixture = new Fixture(true);

        // Act
        var result = await fixture.Policy.AcceptAsync(raw, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.PasswordTooShort);
            await Assert.That(fixture.Breached.Asked).IsEqualTo(0);
            await Assert.That(fixture.Outcomes.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task The_time_the_list_takes_is_recorded_in_seconds_under_the_published_name()
    {
        // Arrange
        using var fixture = new Fixture(false, TimeSpan.FromMilliseconds(250));

        // Act
        await fixture.Policy.AcceptAsync(Valid, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(fixture.Durations.Single()).IsEqualTo(0.25);
            await Assert.That(fixture.Instrument!.Name).IsEqualTo("frappe.identity.breach_check.duration");
            await Assert.That(fixture.Instrument.Unit).IsEqualTo("s");
            await Assert.That(fixture.Instrument.Meter.Name).IsEqualTo("Frappe.Identity");
        }
    }

    [Test]
    public async Task The_tag_of_every_outcome_is_named()
    {
        // Act
        var names = Enum.GetValues<BreachCheckOutcome>().Select(IdentityMetrics.OutcomeName);

        // Assert
        await Assert.That(names).IsEquivalentTo(["clean", "breached", "unavailable"]);
    }

    /// <summary>The policy over a fake list, with the breach check instrument listened to.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        private readonly MeterListener _listener = new();

        public Fixture(bool? answer, TimeSpan? takes = null)
        {
            var factory = _provider.GetRequiredService<IMeterFactory>();
            var time = new FakeTimeProvider();
            Breached = new FakeBreached(answer, () => time.Advance(takes ?? TimeSpan.Zero));
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Scope == factory && instrument.Name == "frappe.identity.breach_check.duration")
                {
                    Instrument = instrument;
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                Durations.Add(value);
                Outcomes.Add((string)tags.ToArray().Single(tag => tag.Key == "frappe.outcome").Value!);
            });
            _listener.Start();
            Policy = new PasswordPolicy(Breached, new IdentityMetrics(factory), time);
        }

        public PasswordPolicy Policy { get; }

        public FakeBreached Breached { get; }

        public List<double> Durations { get; } = [];

        public List<string> Outcomes { get; } = [];

        public Instrument? Instrument { get; private set; }

        public void Dispose()
        {
            _listener.Dispose();
            _provider.Dispose();
        }
    }

    /// <summary>A breach list that gives a fixed answer and counts how often it was asked.</summary>
    private sealed class FakeBreached(bool? answer, Action onAsk) : IBreachedPasswords
    {
        public int Asked { get; private set; }

        public Task<bool?> IsBreachedAsync(Password password, CancellationToken cancellationToken)
        {
            Asked++;
            onAsk();
            return Task.FromResult(answer);
        }
    }
}
