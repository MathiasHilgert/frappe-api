using Frappe.Results;

namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// A search session (S11): a UUID the client generates when the user starts typing and sends with every suggestion and
/// with the selection, which closes it; the next search needs a new one. Providers bill a session as one search.
/// </summary>
public sealed record AddressSession
{
    private AddressSession(string value) => Value = value;

    /// <summary>The UUID in its canonical form: 36 lower-case characters with hyphens.</summary>
    public string Value { get; }

    /// <summary>The session of <paramref name="raw"/>: <c>geo.parameter.required</c> when absent or blank, else a UUID.</summary>
    public static Result<AddressSession> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired("session");
        }

        return Guid.TryParseExact(raw, "D", out var id)
            ? new AddressSession(id.ToString("D"))
            : GeoErrors.AddressSessionInvalid();
    }

    /// <summary>A session of its own, for a resolution that follows no suggestions (another module re-validating an address).</summary>
    public static AddressSession New() => new(Guid.NewGuid().ToString("D"));

    public override string ToString() => Value;
}
