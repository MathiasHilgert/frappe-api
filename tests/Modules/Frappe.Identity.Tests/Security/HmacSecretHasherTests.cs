using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class HmacSecretHasherTests
{
    private const string Key = "a-test-key-that-is-at-least-32-characters";

    private static HmacSecretHasher HasherWith(string key) =>
        new(Options.Create(new SecretHashingOptions { Key = key }));

    [Fact]
    public void Hash_IsDeterministic_SoStoredHashesCanBeComparedByEquality()
    {
        HasherWith(Key).Hash("123456").ShouldBe(HasherWith(Key).Hash("123456"));
    }

    [Fact]
    public void Hash_DiffersPerSecret()
    {
        HasherWith(Key).Hash("123456").ShouldNotBe(HasherWith(Key).Hash("123457"), StringComparer.Ordinal);
    }

    [Fact]
    public void Hash_DependsOnTheKey()
    {
        HasherWith(Key).Hash("123456").ShouldNotBe(HasherWith(Key + "!").Hash("123456"), StringComparer.Ordinal);
    }

    [Fact]
    public void Hash_IsAHexHmacSha256_NotTheSecret()
    {
        var hash = HasherWith(Key).Hash("123456");

        hash.Length.ShouldBe(64);
        hash.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-key")]
    [InlineData("                                        ")]
    public void Options_WithoutALongEnoughKey_AreInvalid(string? key)
    {
        new SecretHashingOptionsValidator().Validate(null, new SecretHashingOptions { Key = key }).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Options_WithALongKey_AreValid()
    {
        new SecretHashingOptionsValidator().Validate(null, new SecretHashingOptions { Key = Key }).Succeeded.ShouldBeTrue();
    }
}
