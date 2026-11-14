using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Frappe.Geo.Domain.Codes;

/// <summary>
/// The shared first step of every code made of ASCII letters and digits (country, currency and subdivision codes):
/// trim, check ASCII, then upper-case. ASCII is checked before upper-casing because some non-ASCII letters upper-case to
/// ASCII ones (the long s <c>ſ</c> to <c>S</c>), which would let <c>ſe</c> pass as <c>SE</c>.
/// </summary>
internal static class AsciiCode
{
    /// <summary>
    /// The trimmed, upper-cased <paramref name="raw"/> in <paramref name="candidate"/>, or <see langword="false"/> when
    /// it is missing or holds a character outside ASCII. The caller still checks the code's own shape.
    /// </summary>
    public static bool TryNormalize(string? raw, [NotNullWhen(true)] out string? candidate)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        candidate = Ascii.IsValid(trimmed) ? trimmed.ToUpperInvariant() : null;
        return candidate is not null;
    }
}
