using System.Buffers.Text;
using System.Text;
using Frappe.Geo.Application.Addresses;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>The id a client gets for a suggestion or an address: opaque, round-trips, and anything else is refused.</summary>
public sealed class AddressReferenceTests
{
    [Test]
    public async Task A_reference_round_trips_and_does_not_show_the_providers_id_as_is()
    {
        var reference = new AddressReference("google", "ChIJ3S-JXmauEmsRUcIaWtf4MzE");

        var encoded = reference.Encode();

        await Assert.That(AddressReference.Decode(encoded)).IsEqualTo(reference);
        await Assert.That(encoded).DoesNotContain("ChIJ3S-JXmauEmsRUcIaWtf4MzE").And.DoesNotContain("google");
        await Assert.That(Base64Url.IsValid(encoded)).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not base64url!")]
    [Arguments("ChIJ3S-JXmauEmsRUcIaWtf4MzE")]
    public async Task Anything_that_is_not_an_encoded_reference_is_refused(string? raw) =>
        await Assert.That(AddressReference.Decode(raw)).IsNull();

    [Test]
    [Arguments("a2:google:ChIJ3S")]
    [Arguments("a1:google")]
    [Arguments("a1::ChIJ3S")]
    [Arguments("a1:google:")]
    [Arguments("a1:google:has spaces")]
    [Arguments("a1:google:ChIJ3S:extra")]
    [Arguments("a1:GOOGLE:ChIJ3S")]
    public async Task A_forged_or_tampered_reference_is_refused(string payload)
    {
        var forged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));

        await Assert.That(AddressReference.Decode(forged)).IsNull();
    }
}
