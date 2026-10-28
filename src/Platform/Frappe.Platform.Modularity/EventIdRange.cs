namespace Frappe.Platform.Modularity;

/// <summary>The inclusive range of log <c>EventId</c>s a module owns, such as 2000–2999.</summary>
public sealed record EventIdRange
{
    /// <summary>Initializes the range from <paramref name="start"/> to <paramref name="end"/>, both included.</summary>
    /// <param name="start">The first EventId; greater than zero, because 0 means no EventId.</param>
    /// <param name="end">The last EventId; not less than <paramref name="start"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty or starts at zero or below.</exception>
    public EventIdRange(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        Start = start;
        End = end;
    }

    /// <summary>Gets the first EventId of the range.</summary>
    public int Start { get; }

    /// <summary>Gets the last EventId of the range.</summary>
    public int End { get; }

    /// <summary>Gets whether this range shares any EventId with <paramref name="other"/>.</summary>
    /// <param name="other">The other range.</param>
    /// <returns><see langword="true"/> when the ranges overlap.</returns>
    public bool Overlaps(EventIdRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start <= other.End && other.Start <= End;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Start}-{End}";
}
