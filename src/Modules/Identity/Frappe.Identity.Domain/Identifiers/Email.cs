using Frappe.Results;

namespace Frappe.Identity.Domain.Identifiers;

/// <summary>
/// An email address a person typed: trimmed, at most 254 characters and shaped like an address (one <c>@</c>, a dotted domain, no spaces). It
/// keeps the address as typed, which mail is sent to, and its normalized form (lower case), which uniqueness and lookup compare. It prints
/// only its domain (DOM-06, SEC-07).
/// </summary>
public sealed record Email
{
    /// <summary>The longest address of RFC 5321.</summary>
    public const int MaxLength = 254;

    private const char At = '@';

    private Email(string address) => Address = address;

    /// <summary>The address as typed, trimmed.</summary>
    public string Address { get; }

    /// <summary>The address lower-cased: what uniqueness and lookup compare.</summary>
    public string NormalizedAddress => Address.ToLowerInvariant();

    /// <summary>The address <paramref name="raw"/> spells, or <see cref="IdentityErrorCodes.EmailInvalid"/>.</summary>
    public static Result<Email> Create(string? raw)
    {
        var address = raw?.Trim();
        return IsAddress(address) ? new Email(address!) : Error.InvalidValue(IdentityErrorCodes.EmailInvalid);
    }

    /// <summary>The address with its local part hidden (<c>***@domain</c>): safe to show in a notice.</summary>
    public string Masked => "***" + Address[Address.IndexOf(At, StringComparison.Ordinal)..];

    public override string ToString() => Masked;

    private static bool IsAddress(string? address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > MaxLength || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        var at = address.IndexOf(At, StringComparison.Ordinal);
        if (at < 1 || at != address.LastIndexOf(At))
        {
            return false;
        }

        var domain = address[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal)
            && domain.Split('.').All(label => label.Length > 0);
    }
}
