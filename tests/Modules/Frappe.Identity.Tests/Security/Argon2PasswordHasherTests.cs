using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class Argon2PasswordHasherTests
{
    // The smallest cost the options accept keeps the tests fast; production uses the OWASP defaults.
    private static readonly Argon2PasswordHasher Hasher = Create();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Hash_ProducesAnArgon2idPhcString()
    {
        var hash = await Hasher.HashAsync("correct horse battery", Ct);

        hash.ShouldStartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        hash.ShouldNotContain("correct horse battery");
    }

    [Fact]
    public async Task Hash_SaltsEveryCall()
    {
        var first = await Hasher.HashAsync("correct horse battery", Ct);

        first.ShouldNotBe(await Hasher.HashAsync("correct horse battery", Ct), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Verify_AcceptsTheSamePassword()
    {
        var hash = await Hasher.HashAsync("correct horse battery", Ct);

        (await Hasher.VerifyAsync("correct horse battery", hash, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Verify_RejectsAnotherPassword()
    {
        var hash = await Hasher.HashAsync("correct horse battery", Ct);

        (await Hasher.VerifyAsync("Correct horse battery", hash, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=x,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaA")]
    public async Task Verify_RejectsAMalformedHash(string hash)
    {
        (await Hasher.VerifyAsync("correct horse battery", hash, Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1024, 2, 1)]
    [InlineData(19456, 1, 1)]
    [InlineData(19456, 2, 0)]
    [InlineData(19456, 2, 17)]
    public void Options_BelowTheMinimumCost_AreInvalid(int memory, int iterations, int parallelism)
    {
        var options = new Argon2Options { MemorySizeKib = memory, Iterations = iterations, Parallelism = parallelism };

        new Argon2OptionsValidator().Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public async Task NeedsRehash_OnlyForAHashWeakerThanTheCurrentCost()
    {
        var current = await Hasher.HashAsync("correct horse battery", Ct);
        using var stronger = Create(iterations: Argon2Options.MinimumIterations + 1);

        Hasher.NeedsRehash(current).ShouldBeFalse();
        stronger.NeedsRehash(current).ShouldBeTrue();
        Hasher.NeedsRehash("not-a-hash").ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentCalls_BeyondTheBound_WaitForASlotAndAllComplete()
    {
        using var single = Create(maxConcurrentHashes: 1);

        var hashes = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => single.HashAsync("correct horse battery", Ct)));

        foreach (var hash in hashes)
            (await single.VerifyAsync("correct horse battery", hash, Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task NoSlotFreeingInTime_IsUnavailable_WithARetryAfter()
    {
        using var single = Create(maxConcurrentHashes: 1, slotWaitMilliseconds: 1);
        var slots = (SemaphoreSlim)typeof(Argon2PasswordHasher)
            .GetField("_slots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(single)!;
        await slots.WaitAsync(Ct); // another request holds the only slot

        var busy = await Should.ThrowAsync<Frappe.SharedKernel.Validation.UnavailableException>(
            () => single.HashAsync("correct horse battery", Ct));

        busy.Error.Is(Frappe.Identity.Domain.IdentityErrors.PasswordHashingBusy).ShouldBeTrue();
        busy.Error.RetryAfter.ShouldBe(TimeSpan.FromSeconds(1));
        slots.Release();
        (await single.HashAsync("correct horse battery", Ct)).ShouldStartWith("$argon2id$");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_025)]
    public void Options_WithAnOutOfRangeConcurrencyBound_AreInvalid(int maxConcurrentHashes)
    {
        new Argon2OptionsValidator().Validate(null, new Argon2Options { MaxConcurrentHashes = maxConcurrentHashes })
            .Failed.ShouldBeTrue();
    }

    [Fact]
    public void Options_Defaults_AreValid()
    {
        new Argon2OptionsValidator().Validate(null, new Argon2Options()).Succeeded.ShouldBeTrue();
    }

    private static Argon2PasswordHasher Create(
        int iterations = Argon2Options.MinimumIterations, int maxConcurrentHashes = 4, int slotWaitMilliseconds = 2_000) =>
        new(Options.Create(new Argon2Options
        {
            MemorySizeKib = Argon2Options.MinimumMemorySizeKib,
            Iterations = iterations,
            Parallelism = 1,
            MaxConcurrentHashes = maxConcurrentHashes,
            SlotWaitMilliseconds = slotWaitMilliseconds,
        }));
}
