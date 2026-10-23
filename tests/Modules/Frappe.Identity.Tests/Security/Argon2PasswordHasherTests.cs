using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class Argon2PasswordHasherTests
{
    // The smallest cost the options accept keeps the tests fast; production uses the OWASP defaults.
    private static readonly Argon2PasswordHasher Hasher = new(Options.Create(new Argon2Options
    {
        MemorySizeKib = Argon2Options.MinimumMemorySizeKib,
        Iterations = Argon2Options.MinimumIterations,
        Parallelism = 1,
    }));

    [Fact]
    public void Hash_ProducesAnArgon2idPhcString()
    {
        var hash = Hasher.Hash("correct horse battery");

        hash.ShouldStartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        hash.ShouldNotContain("correct horse battery");
    }

    [Fact]
    public void Hash_SaltsEveryCall()
    {
        Hasher.Hash("correct horse battery").ShouldNotBe(Hasher.Hash("correct horse battery"), StringComparer.Ordinal);
    }

    [Fact]
    public void Verify_AcceptsTheSamePassword()
    {
        var hash = Hasher.Hash("correct horse battery");

        Hasher.Verify("correct horse battery", hash).ShouldBeTrue();
    }

    [Fact]
    public void Verify_RejectsAnotherPassword()
    {
        var hash = Hasher.Hash("correct horse battery");

        Hasher.Verify("Correct horse battery", hash).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=x,t=2,p=1$c2FsdHNhbHRzYWx0c2FsdA$aGFzaA")]
    public void Verify_RejectsAMalformedHash(string hash)
    {
        Hasher.Verify("correct horse battery", hash).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1024, 2, 1)]
    [InlineData(19456, 1, 1)]
    [InlineData(19456, 2, 0)]
    public void Options_BelowTheMinimumCost_AreInvalid(int memory, int iterations, int parallelism)
    {
        new Argon2Options { MemorySizeKib = memory, Iterations = iterations, Parallelism = parallelism }
            .IsValid().ShouldBeFalse();
    }

    [Fact]
    public void Options_Defaults_AreValid()
    {
        new Argon2Options().IsValid().ShouldBeTrue();
    }
}
