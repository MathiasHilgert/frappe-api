namespace Frappe.Identity.Domain;

/// <summary>The error codes of the Identity module (DOM-03). A published code is never renamed or reused.</summary>
public static class IdentityErrorCodes
{
    /// <summary>A password has fewer than <see cref="Credentials.Password.MinLength"/> characters (parameter <c>min</c>).</summary>
    public const string PasswordTooShort = "identity.password.too_short";

    /// <summary>A password has more than <see cref="Credentials.Password.MaxLength"/> characters (parameter <c>max</c>).</summary>
    public const string PasswordTooLong = "identity.password.too_long";

    /// <summary>A password appears in a public breach list.</summary>
    public const string PasswordBreached = "identity.password.breached";
}
