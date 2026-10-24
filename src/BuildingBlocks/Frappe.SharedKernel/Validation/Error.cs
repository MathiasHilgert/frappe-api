namespace Frappe.SharedKernel.Validation;

/// <summary>
/// A broken rule. <see cref="Code"/> is stable and machine-readable and doubles as the message catalogue key;
/// <see cref="Message"/> is the English fallback; <see cref="Field"/> points at the offending input when there is one.
/// Two errors are equal when their <see cref="Code"/> and <see cref="Field"/> are: together they name the rule and where it
/// was broken, while the message, kind and the occurrence details (<see cref="RetryAfter"/>, <see cref="Metadata"/>)
/// describe one breach of it.
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

    public bool Equals(Error? other) =>
        other is not null
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Field, other.Field, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(Code), Field is null ? 0 : StringComparer.Ordinal.GetHashCode(Field));
}
