using Frappe.Identity.Infrastructure.Security;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class RandomSecretGeneratorTests
{
    private readonly RandomSecretGenerator _generator = new();

    [Fact]
    public void NewVerificationCode_IsSixDigits()
    {
        for (var i = 0; i < 200; i++)
        {
            _generator.NewVerificationCode().ShouldMatch("^[0-9]{6}$");
        }
    }

    [Fact]
    public void NewSessionToken_Is256BitsOfUrlSafeBase64()
    {
        var token = _generator.NewSessionToken();

        token.Length.ShouldBe(43);
        token.ShouldMatch("^[A-Za-z0-9_-]{43}$");
    }

    [Fact]
    public void NewSessionToken_IsUnique()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => _generator.NewSessionToken()).ToHashSet(StringComparer.Ordinal);

        tokens.Count.ShouldBe(100);
    }
}
