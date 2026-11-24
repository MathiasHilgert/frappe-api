using System.Text.RegularExpressions;
using Frappe.Results;

namespace Frappe.Notifications.Domain.Deliveries;

/// <summary>
/// The name of a delivery channel (<c>email</c>, <c>sms</c>, <c>push</c>): an open value any channel plug-in may introduce, never a closed
/// set, so adding a channel changes no core type (S0). Lower snake case, at most <see cref="MaxLength"/> characters, as the column holds it.
/// </summary>
public sealed partial record ChannelName
{
    /// <summary>The column width of a stored channel name.</summary>
    public const int MaxLength = 30;

    private ChannelName(string value) => Value = value;

    public string Value { get; }

    public static Result<ChannelName> Create(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length is 0 or > MaxLength || !Shape().IsMatch(value))
        {
            return Error.InvalidValue("notifications.channel.invalid_format", ("value", raw));
        }

        return new ChannelName(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
