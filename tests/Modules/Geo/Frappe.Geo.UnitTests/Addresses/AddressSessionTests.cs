using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Domain;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>A search session is a UUID the client generates and sends with every keystroke and with the selection.</summary>
public sealed class AddressSessionTests
{
    [Test]
    public async Task A_UUID_is_a_session_kept_in_lower_case()
    {
        var session = AddressSession.Create("5B1C1A5E-3F5D-4E0B-9F53-4D1C2B9E8A10");

        await Assert.That(session.IsSuccess).IsTrue();
        await Assert.That(session.Value.Value).IsEqualTo("5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10");
    }

    [Test]
    [Arguments(null, GeoErrorCodes.ParameterRequired)]
    [Arguments("", GeoErrorCodes.ParameterRequired)]
    [Arguments(" ", GeoErrorCodes.ParameterRequired)]
    [Arguments("abc", GeoErrorCodes.AddressSessionInvalid)]
    [Arguments("5b1c1a5e3f5d4e0b9f534d1c2b9e8a10", GeoErrorCodes.AddressSessionInvalid)]
    [Arguments("{5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10}", GeoErrorCodes.AddressSessionInvalid)]
    public async Task A_missing_session_is_required_and_anything_else_is_refused(string? raw, string code)
    {
        var session = AddressSession.Create(raw);

        await Assert.That(session.IsSuccess).IsFalse();
        await Assert.That(session.Errors.Single().Code).IsEqualTo(code);
    }
}
