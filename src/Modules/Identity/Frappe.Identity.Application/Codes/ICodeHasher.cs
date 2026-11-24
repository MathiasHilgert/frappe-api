using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// Turns a one-time code into the keyed hash the database keeps, and checks a typed code against one (DES-03: the scheme is a strategy).
/// </summary>
public interface ICodeHasher
{
    /// <summary>The HMAC of <paramref name="code"/> under the configured key.</summary>
    CodeHmac Hash(string code);

    /// <summary>Whether <paramref name="candidate"/> is the code <paramref name="stored"/> was made from, compared in fixed time.</summary>
    bool Matches(CodeHmac stored, string candidate);
}
