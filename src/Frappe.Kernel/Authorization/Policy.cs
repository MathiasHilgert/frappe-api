using System.Text.RegularExpressions;

namespace Frappe.Kernel.Authorization;

/// <summary>
/// The permission a message requires before its handler runs.
/// </summary>
public sealed partial record Policy
{
    /// <summary>Initializes a policy that requires <paramref name="permission"/>.</summary>
    /// <param name="permission">Lowercase dot-separated segments, such as <c>order.tab.open</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="permission"/> is not in that format.</exception>
    public Policy(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (!PermissionFormat().IsMatch(permission))
        {
            throw new ArgumentException(
                $"'{permission}' is not a permission: use lowercase dot-separated segments such as 'order.tab.open'.",
                nameof(permission)
            );
        }

        Permission = permission;
    }

    // Reserved values start with '$', which the public constructor rejects, so no permission can equal them.
    private Policy() => Permission = string.Empty;

    /// <summary>Gets the policy whose target is the caller itself.</summary>
    public static Policy Self { get; } = new() { Permission = "$self" };

    /// <summary>Gets the policy that requires no principal.</summary>
    public static Policy Anonymous { get; } = new() { Permission = "$anonymous" };

    /// <summary>Gets the required permission.</summary>
    public string Permission { get; private init; }

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100
    )]
    private static partial Regex PermissionFormat();
}
