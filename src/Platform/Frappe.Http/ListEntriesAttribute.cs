using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Frappe.Http;

/// <summary>What each entry of a comma separated configuration list must be.</summary>
internal enum ListEntryKind
{
    /// <summary>An IPv4 or IPv6 literal.</summary>
    IpAddress,

    /// <summary>A network in CIDR notation.</summary>
    IpNetwork,

    /// <summary>An <c>https</c> origin, or <c>http</c> on a loopback host.</summary>
    ConsoleOrigin,
}

/// <summary>
/// A comma separated list whose every entry must parse, rejected with a message that names the full configuration key and
/// every bad entry. The generated validator ignores the <c>ErrorMessage</c> of the built-in attributes, so it is built here.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class ListEntriesAttribute(string configurationKey, ListEntryKind kind) : ValidationAttribute
{
    /// <summary>The non-empty, trimmed entries of a comma separated list.</summary>
    public static string[] Entries(string? list) =>
        (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var invalid = Entries(value as string).Where(entry => !Parses(entry)).Select(entry => $"'{entry}'").ToArray();
        return invalid.Length == 0
            ? ValidationResult.Success
            : new ValidationResult($"Configuration '{configurationKey}' {Problem}: {string.Join(", ", invalid)}.");
    }

    private string Problem => kind switch
    {
        ListEntryKind.IpAddress => "has an entry that is not an IP address",
        ListEntryKind.IpNetwork => "has an entry that is not a network in CIDR notation",
        _ => "has an entry that is not an https origin (or http on localhost)",
    };

    private bool Parses(string entry) => kind switch
    {
        ListEntryKind.IpAddress => IPAddress.TryParse(entry, out _),
        ListEntryKind.IpNetwork => IPNetwork.TryParse(entry, out _),
        _ => ConsoleOriginOptions.TryNormalize(entry, out _),
    };
}
