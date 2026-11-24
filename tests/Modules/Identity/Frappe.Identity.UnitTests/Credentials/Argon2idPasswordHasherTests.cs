using System.Diagnostics.Metrics;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Infrastructure.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>Argon2id under OWASP's parameters, in its standard encoding, with replacement when the parameters change (S4).</summary>
public sealed class Argon2idPasswordHasherTests
{
    private const string Text = "correct horse battery";

    private static Password Pass(string text = Text) => Password.Create(text).Value;

    private static Argon2idPasswordHasher Hasher(PasswordOptions? options = null, IdentityMetrics? metrics = null)
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        return new Argon2idPasswordHasher(
            Options.Create(options ?? new PasswordOptions()),
            metrics ?? new IdentityMetrics(provider.GetRequiredService<IMeterFactory>()),
            TimeProvider.System);
    }

    [Test]
    public async Task A_hash_is_standard_encoded_Argon2id_with_19_MiB_two_passes_and_one_lane()
    {
        // Act
        var hash = Hasher().Hash(Pass());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hash.Value).StartsWith("$argon2id$v=19$m=19456,t=2,p=1$");
            await Assert.That(hash.Value).DoesNotContain(Text);
            await Assert.That(hash.Value).DoesNotContain('\0');
        }
    }

    [Test]
    public async Task The_same_password_hashed_twice_gives_two_hashes_that_both_verify()
    {
        // Arrange
        var hasher = Hasher();

        // Act
        var first = hasher.Hash(Pass());
        var second = hasher.Hash(Pass());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Value).IsNotEqualTo(second.Value);
            await Assert.That(hasher.Verify(first, Text)).IsTrue();
            await Assert.That(hasher.Verify(second, Text)).IsTrue();
        }
    }

    [Test]
    public async Task A_password_of_any_script_round_trips_and_a_different_one_does_not_verify()
    {
        // Arrange
        var hasher = Hasher();
        var hash = hasher.Hash(Pass("contraseña-密码-\U0001F600-ok"));

        // Act
        var same = hasher.Verify(hash, "contraseña-密码-\U0001F600-ok");
        var other = hasher.Verify(hash, "contrasena-密码-\U0001F600-ok");
        var shorter = hasher.Verify(hash, "x");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(same).IsTrue();
            await Assert.That(other).IsFalse();
            await Assert.That(shorter).IsFalse();
        }
    }

    [Test]
    [Arguments("not a hash")]
    [Arguments("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA")]
    [Arguments("$bcrypt$whatever")]
    public async Task A_stored_value_that_is_not_a_usable_hash_never_verifies_and_never_throws(string stored)
    {
        // Arrange
        var hasher = Hasher();

        // Act
        var verified = hasher.Verify(new PasswordHash(stored), Text);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(verified).IsFalse();
            await Assert.That(hasher.NeedsRehash(new PasswordHash(stored))).IsFalse();
        }
    }

    [Test]
    public async Task A_stored_value_longer_than_any_hash_never_verifies()
    {
        // Act
        var verified = Hasher().Verify(new PasswordHash(new string('a', 500)), Text);

        // Assert
        await Assert.That(verified).IsFalse();
    }

    [Test]
    public async Task A_hash_is_current_under_the_parameters_that_made_it_and_due_for_replacement_under_others()
    {
        // Arrange
        var hash = Hasher().Hash(Pass());

        // Act
        var current = Hasher().NeedsRehash(hash);
        var moreIterations = Hasher(new PasswordOptions { Iterations = 3 }).NeedsRehash(hash);
        var moreMemory = Hasher(new PasswordOptions { MemoryMebibytes = 20 }).NeedsRehash(hash);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(current).IsFalse();
            await Assert.That(moreIterations).IsTrue();
            await Assert.That(moreMemory).IsTrue();
        }
    }

    [Test]
    public async Task A_hash_made_under_old_parameters_still_verifies_under_new_ones()
    {
        // Arrange
        var old = Hasher(new PasswordOptions { Iterations = 1, MemoryMebibytes = 8 }).Hash(Pass());
        var current = Hasher();

        // Act
        var verified = current.Verify(old, Text);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(old.Value).StartsWith("$argon2id$v=19$m=8192,t=1,p=1$");
            await Assert.That(verified).IsTrue();
            await Assert.That(current.NeedsRehash(old)).IsTrue();
        }
    }

    [Test]
    public async Task Hashing_and_verifying_each_record_their_duration_in_seconds_under_the_published_name()
    {
        // Arrange
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = provider.GetRequiredService<IMeterFactory>();
        var recorded = new List<(string Name, string Unit, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Scope == factory && instrument.Name == "frappe.identity.password_hash.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => recorded.Add((instrument.Name, instrument.Unit!, value)));
        listener.Start();
        var hasher = Hasher(metrics: new IdentityMetrics(factory));

        // Act
        var hash = hasher.Hash(Pass());
        hasher.Verify(hash, Text);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(recorded.Count).IsEqualTo(2);
            await Assert.That(recorded.All(r => r is { Name: "frappe.identity.password_hash.duration", Unit: "s" } && r.Value > 0)).IsTrue();
        }
    }
}
