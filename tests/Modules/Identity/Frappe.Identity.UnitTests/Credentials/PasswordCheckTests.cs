using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Credentials;
using Microsoft.Extensions.Time.Testing;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>A refusal is never faster than 500 ms and costs one hash whatever the reason; a match answers at once (S4, SEC-06).</summary>
public sealed class PasswordCheckTests
{
    private static readonly PasswordHash Stored = new("stored-hash");

    [Test]
    public async Task A_match_answers_at_once_without_waiting_for_the_floor()
    {
        // Arrange
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, new FakeTimeProvider());

        // Act
        var task = check.MatchesAsync(Stored, "right password", default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(task.IsCompletedSuccessfully).IsTrue();
            await Assert.That(await task).IsTrue();
        }
    }

    [Test]
    public async Task A_wrong_password_is_refused_only_once_the_floor_has_passed()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var check = new PasswordCheck(new FakeHasher("right password"), time);

        // Act
        var task = check.MatchesAsync(Stored, "wrong password", default);
        time.Advance(TimeSpan.FromMilliseconds(499));
        var early = task.IsCompleted;
        time.Advance(TimeSpan.FromMilliseconds(1));
        var matched = await task;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(early).IsFalse();
            await Assert.That(matched).IsFalse();
        }
    }

    [Test]
    public async Task An_unknown_subject_verifies_a_dummy_hash_made_by_the_live_hasher_and_is_refused_after_the_floor()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, time);

        // Act
        var task = check.MatchesAsync(null, "right password", default);
        var early = task.IsCompleted;
        time.Advance(PasswordCheck.RefusalFloor);
        var matched = await task;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(early).IsFalse();
            await Assert.That(matched).IsFalse();
            await Assert.That(hasher.Hashed).IsEqualTo(1);
            await Assert.That(hasher.Verified.Count).IsEqualTo(1);
            await Assert.That(hasher.Verified[0].Hash).IsEqualTo(hasher.LastHash);
            await Assert.That(hasher.Verified[0].Hash).IsNotEqualTo(Stored);
        }
    }

    [Test]
    public async Task The_dummy_hash_is_made_once_however_many_unknown_subjects_are_checked()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, time);

        // Act
        var first = check.MatchesAsync(null, "some password", default);
        var second = check.MatchesAsync(null, "other password", default);
        time.Advance(PasswordCheck.RefusalFloor);
        await Task.WhenAll(first, second);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hasher.Hashed).IsEqualTo(1);
            await Assert.That(hasher.Verified.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task The_time_the_hash_took_counts_toward_the_floor()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password") { OnVerify = () => time.Advance(TimeSpan.FromMilliseconds(300)) };
        var check = new PasswordCheck(hasher, time);

        // Act
        var task = check.MatchesAsync(Stored, "wrong password", default);
        time.Advance(TimeSpan.FromMilliseconds(199));
        var early = task.IsCompleted;
        time.Advance(TimeSpan.FromMilliseconds(1));
        await task;

        // Assert
        await Assert.That(early).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task An_empty_candidate_is_refused_after_the_floor_without_hashing(string? candidate)
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, time);

        // Act
        var task = check.MatchesAsync(Stored, candidate, default);
        var early = task.IsCompleted;
        time.Advance(PasswordCheck.RefusalFloor);
        var matched = await task;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(early).IsFalse();
            await Assert.That(matched).IsFalse();
            await Assert.That(hasher.Verified.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_candidate_too_long_to_be_a_password_is_not_hashed_and_still_waits_for_the_floor()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, time);

        // Act
        var task = check.MatchesAsync(Stored, new string('a', 129), default);
        var early = task.IsCompleted;
        time.Advance(PasswordCheck.RefusalFloor);
        var matched = await task;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(early).IsFalse();
            await Assert.That(matched).IsFalse();
            await Assert.That(hasher.Verified.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_short_wrong_attempt_is_a_plain_refusal_not_a_length_error()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var hasher = new FakeHasher("right password");
        var check = new PasswordCheck(hasher, time);

        // Act
        var task = check.MatchesAsync(Stored, "abc", default);
        time.Advance(PasswordCheck.RefusalFloor);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(await task).IsFalse();
            await Assert.That(hasher.Verified.Single().Candidate).IsEqualTo("abc");
        }
    }

    [Test]
    public async Task Giving_up_while_waiting_for_the_floor_cancels_the_wait()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var check = new PasswordCheck(new FakeHasher("right password"), new FakeTimeProvider());
        var task = check.MatchesAsync(Stored, "wrong password", cancellation.Token);

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }

    /// <summary>A hasher that knows one correct password and records every hash and verification asked of it.</summary>
    private sealed class FakeHasher(string correct) : IPasswordHasher
    {
        public int Hashed { get; private set; }

        public PasswordHash? LastHash { get; private set; }

        public Action? OnVerify { get; init; }

        public List<(PasswordHash Hash, string Candidate)> Verified { get; } = [];

        public PasswordHash Hash(Password password)
        {
            Hashed++;
            LastHash = new PasswordHash("dummy-hash");
            return LastHash;
        }

        public bool Verify(PasswordHash hash, string candidate)
        {
            Verified.Add((hash, candidate));
            OnVerify?.Invoke();
            return hash == Stored && candidate == correct;
        }

        public bool NeedsRehash(PasswordHash hash) => false;
    }
}
