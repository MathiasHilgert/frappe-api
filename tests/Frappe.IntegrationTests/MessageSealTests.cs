using System.Security.Cryptography;
using Frappe.Platform.Messaging.Sealing;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.IntegrationTests;

public sealed class MessageSealTests
{
    private static readonly AesGcmMessageSeal Seal = Create(new string('k', 32));

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

    [Fact]
    public void Seal_StartsWithTheFormatVersionAndTheKeyId()
    {
        var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(Seal.Seal("123456"));

        bytes[0].ShouldBe((byte)2);
        bytes[1].ShouldBe(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(Seal.Seal("654321"))[1]);
    }

    [Fact]
    public void Open_AfterRotation_StillOpensWhatThePreviousKeySealed()
    {
        var before = Create(new string('k', 32));
        var sealedCode = before.Seal("123456");

        var rotated = Create(new string('n', 32), previousKey: new string('k', 32));

        rotated.Open(sealedCode).ShouldBe("123456");
        rotated.Open(rotated.Seal("654321")).ShouldBe("654321");
        Should.Throw<CryptographicException>(() => before.Open(rotated.Seal("654321")));
    }

    [Fact]
    public void Open_OnceThePreviousKeyIsDropped_RefusesItsSeals()
    {
        var sealedCode = Create(new string('k', 32)).Seal("123456");

        Should.Throw<CryptographicException>(() => Create(new string('n', 32)).Open(sealedCode));
    }

    private static AesGcmMessageSeal Create(string key, string? previousKey = null) =>
        new(Options.Create(new MessageSealOptions { Key = key, PreviousKey = previousKey }));
}
