namespace Frappe.Results;

/// <summary>
/// A business error: a stable, machine-readable <see cref="Code"/> such as <c>geo.holiday_year.out_of_range</c>
/// plus the typed <see cref="Parameters"/> that explain it. It carries no human text and no transport details:
/// adapters decide how to present it (an HTTP status, a localized message, a rejected message).
/// Codes are part of the public contract: never reuse or rename one.
/// The hierarchy is <c>closed</c> (C# 15): every kind of error is declared in this assembly, so a <c>switch</c> over the
/// kinds is exhaustive and a new kind is a compile error wherever it is not handled yet.
/// </summary>
public closed record Error
{
    protected Error(string code, IReadOnlyCollection<(string Name, object? Value)> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(parameters);

        var byName = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            if (!byName.TryAdd(name, value))
            {
                throw new ArgumentException($"Parameter '{name}' is declared twice for error '{code}'.", nameof(parameters));
            }
        }

        Code = code;
        Parameters = byName;
    }

    /// <summary>Stable identifier, lower case, dot separated, prefixed by the module.</summary>
    public string Code { get; }

    /// <summary>
    /// Facts needed to explain or recover from the error, for example <c>min</c> and <c>max</c>.
    /// They are sent to API clients as they are, so use only client-safe values: strings, numbers,
    /// booleans, dates and arrays of those. Never put an entity or any object with internal state here.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; }

    /// <summary>
    /// Value equality: same error type, same code, same parameters (any order; arrays and other sequences compared
    /// by content). The record-generated equality would compare the parameter dictionary by reference.
    /// </summary>
    public virtual bool Equals(Error? other) =>
        other is not null
        && EqualityContract == other.EqualityContract
        && Code == other.Code
        && Parameters.Count == other.Parameters.Count
        && Parameters.All(p => other.Parameters.TryGetValue(p.Key, out var value) && SameValue(p.Value, value));

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(EqualityContract, Code, Parameters.Count);
        foreach (var name in Parameters.Keys.Order(StringComparer.Ordinal))
        {
            hash = HashCode.Combine(hash, name);
        }

        return hash;
    }

    /// <summary>
    /// Strings compare as text (never as a sequence of chars); other sequences compare element by element,
    /// recursively; everything else uses its own equality (so <c>1</c> and <c>1L</c> differ, like in C#).
    /// </summary>
    private static bool SameValue(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (string, _) or (_, string) => Equals(left, right),
        (System.Collections.IEnumerable a, System.Collections.IEnumerable b) =>
            a.Cast<object?>().SequenceEqual(b.Cast<object?>(), ValueComparer.Instance),
        _ => Equals(left, right),
    };

    private sealed class ValueComparer : IEqualityComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public new bool Equals(object? x, object? y) => SameValue(x, y);

        public int GetHashCode(object? obj) => 0;
    }

    /// <summary>A value object rejected its input.</summary>
    public static InvalidValueError InvalidValue(string code, params (string Name, object? Value)[] parameters) =>
        new(code, parameters);

    /// <summary>A business rule or an invariant refused the operation.</summary>
    public static RuleViolationError RuleViolation(string code, params (string Name, object? Value)[] parameters) =>
        new(code, parameters);
}

/// <summary>A value object rejected its input (wrong format, out of range, empty).</summary>
public sealed record InvalidValueError : Error
{
    public InvalidValueError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}

/// <summary>A business rule or an invariant refused the operation, although every value was valid on its own.</summary>
public sealed record RuleViolationError : Error
{
    public RuleViolationError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}

/// <summary>The use case looked for something that does not exist.</summary>
public sealed record NotFoundError : Error
{
    public NotFoundError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}

/// <summary>
/// A dependency the use case needs is not configured or not answering (an external provider without credentials, a
/// timeout, an outage). The request may succeed later, unchanged.
/// </summary>
public sealed record UnavailableError : Error
{
    public UnavailableError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}

/// <summary>The use case cannot proceed because of the current state (a duplicate, a concurrent change).</summary>
public sealed record ConflictError : Error
{
    public ConflictError(string code, params (string Name, object? Value)[] parameters)
        : base(code, parameters)
    {
    }
}
