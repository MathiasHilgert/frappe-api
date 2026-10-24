namespace Frappe.SharedKernel.Validation;

/// <summary>
/// A broken rule. <see cref="Code"/> is stable and machine-readable and doubles as the message catalogue key;
/// <see cref="Message"/> is the English fallback; <see cref="Field"/> points at the offending input when there is one.
/// Equality is the default record equality over every member. To ask "is this the same broken rule?" use
/// <see cref="ByRule"/>, which compares only <see cref="Code"/> and <see cref="Field"/>: together they name the rule and
/// where it was broken, while the message, kind and occurrence details describe one breach of it.
/// </summary>
public sealed record Error(string Code, string Message, string? Field = null, ErrorKind Kind = ErrorKind.Validation)
{
    /// <summary>How long to wait before trying again; set on a <see cref="ErrorKind.TooManyRequests"/> error.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Extra facts about this occurrence for the edge to disclose (e.g. an id), keyed by name.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>This error, telling the caller to wait <paramref name="wait"/> before trying again.</summary>
    public Error WithRetryAfter(TimeSpan wait) => this with { RetryAfter = wait };

    /// <summary>This error with <paramref name="key"/> set to <paramref name="value"/> in its <see cref="Metadata"/>.</summary>
    public Error WithMetadata(string key, object? value) =>
        this with
        {
            Metadata = new Dictionary<string, object?>(Metadata ?? new Dictionary<string, object?>(StringComparer.Ordinal), StringComparer.Ordinal)
            {
                [key] = value,
            },
        };

    /// <summary>Compares errors by the rule they break: <see cref="Code"/> and <see cref="Field"/>, ordinal.</summary>
    public static IEqualityComparer<Error> ByRule { get; } = new RuleComparer();

    /// <summary>Whether this error breaks the same rule as <paramref name="other"/> (see <see cref="ByRule"/>).</summary>
    public bool Is(Error other) => ByRule.Equals(this, other);

    private sealed class RuleComparer : IEqualityComparer<Error>
    {
        public bool Equals(Error? x, Error? y) =>
            ReferenceEquals(x, y)
            || (x is not null
                && y is not null
                && string.Equals(x.Code, y.Code, StringComparison.Ordinal)
                && string.Equals(x.Field, y.Field, StringComparison.Ordinal));

        public int GetHashCode(Error obj)
        {
            ArgumentNullException.ThrowIfNull(obj);
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Code),
                obj.Field is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Field));
        }
    }
}
