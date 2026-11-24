using System.Text.RegularExpressions;

namespace Frappe.Notifications.Application.Kinds;

/// <summary>
/// Every notification kind the host knows, by name. Building it checks what a typo would break later: kind names are unique and lower
/// snake case, so a host with a bad catalog fails at startup, not at the first send.
/// </summary>
public sealed partial class NotificationCatalog
{
    private const int MaxKindLength = 100;

    private readonly Dictionary<string, NotificationKind> _byName = [with(StringComparer.Ordinal)];

    /// <exception cref="InvalidOperationException">A kind name is repeated or is not lower snake case.</exception>
    public NotificationCatalog(IEnumerable<NotificationKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        foreach (var kind in kinds)
        {
            if (kind.Name.Length > MaxKindLength || !Snake().IsMatch(kind.Name))
            {
                throw new InvalidOperationException(
                    $"Notification kind '{kind.Name}' must be lower snake case of at most {MaxKindLength} characters.");
            }

            if (!_byName.TryAdd(kind.Name, kind))
            {
                throw new InvalidOperationException($"Notification kind '{kind.Name}' is declared twice.");
            }
        }
    }

    public IReadOnlyCollection<NotificationKind> Kinds => _byName.Values;

    public bool TryGet(string name, out NotificationKind kind)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out kind!);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Snake();
}
