using System.Security.Cryptography;
using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class AesGcmOwnerCodeSealTests
{
    private static readonly AesGcmOwnerCodeSeal Seal = Create(new string('k', 32));

    [Fact]
    public void Seal_HidesTheCode_AndOpenGivesItBack()
    {
        var sealedCode = Seal.Seal("123456");

        sealedCode.ShouldNotContain("123456");
        Seal.Open(sealedCode).ShouldBe("123456");
    }

    [Fact]
    public void Seal_IsRandomized_SoEqualCodesLookDifferent() =>
        Seal.Seal("123456").ShouldNotBe(Seal.Seal("123456"), StringComparer.Ordinal);

    [Fact]
    public void Open_ATamperedSeal_Throws()
    {
        var sealedCode = Seal.Seal("123456").ToCharArray();
        sealedCode[^3] = sealedCode[^3] == 'A' ? 'B' : 'A';

        Should.Throw<CryptographicException>(() => Seal.Open(new string(sealedCode)));
    }

    [Fact]
    public void Open_ASealFromAnotherKey_Throws() =>
        Should.Throw<CryptographicException>(() => Create(new string('x', 32)).Open(Seal.Seal("123456")));

    private static AesGcmOwnerCodeSeal Create(string key) => new(Options.Create(new SecretHashingOptions { Key = key }));
}
