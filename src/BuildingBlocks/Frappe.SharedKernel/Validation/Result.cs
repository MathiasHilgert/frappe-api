using System.Diagnostics.CodeAnalysis;

namespace Frappe.SharedKernel.Validation;

/// <summary>
/// The outcome of an operation that returns nothing: success, or every rule it broke. <see cref="Result{T}"/> is a
/// separate type (not a subclass), so each declares its own <c>IsSuccess</c> and flow analysis needs no shadowing.
/// </summary>
public sealed class Result
{
    private static readonly Result Ok = new([]);

    private Result(IReadOnlyList<Error> errors) => Errors = errors;

    /// <summary>A snapshot: later changes to the originating notification or list never leak into it.</summary>
    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;
    public bool IsFailure => !IsSuccess;

    /// <summary>Whether any error breaks <paramref name="rule"/> (see <see cref="Error.Is"/>).</summary>
    public bool Has(Error rule) => Errors.Any(e => e.Is(rule));

    public static Result Success() => Ok;
    public static Result<T> Success<T>(T value) => new(value, []);

    public static Result Failure(ValidationNotification notification) => new(Snapshot(notification));
    public static Result Failure(Error error) => new([error]);

    /// <summary>Propagates every error of another failure (never just the first).</summary>
    public static Result Failure(IEnumerable<Error> errors) => new(Snapshot(errors));

    public static Result<T> Failure<T>(ValidationNotification notification) => new(default, Snapshot(notification));
    public static Result<T> Failure<T>(Error error) => new(default, [error]);

    /// <summary>Propagates every error of another failure (never just the first).</summary>
    public static Result<T> Failure<T>(IEnumerable<Error> errors) => new(default, Snapshot(errors));

    /// <summary>Validates every rule into one notification and builds the value only when none broke.</summary>
    public static Result<T> From<T>(ValidationNotification notification, Func<T> create) =>
        notification.HasErrors ? Failure<T>(notification) : Success(create());

    private static Error[] Snapshot(ValidationNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return notification.HasErrors
            ? [.. notification.Errors]
            : throw new ArgumentException("A failure needs at least one error.", nameof(notification));
    }

    private static Error[] Snapshot(IEnumerable<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Error[] copy = [.. errors];
        return copy.Length > 0 ? copy : throw new ArgumentException("A failure needs at least one error.", nameof(errors));
    }
}

/// <summary>The outcome of an operation that returns a <typeparamref name="T"/>: the value, or every rule it broke.</summary>
/// <remarks>
/// A value and an <see cref="Error"/> convert implicitly, so a handler reads <c>return value;</c> or
/// <c>return SomeErrors.Rule;</c>. The conversion from <typeparamref name="T"/> stays because every caller returns a
/// concrete value type that can never itself be an <see cref="Error"/>; use <see cref="Result.Success{T}"/> when the
/// intent would otherwise be unclear.
/// </remarks>
public sealed class Result<T>
{
    private readonly T? _value;

    internal Result(T? value, IReadOnlyList<Error> errors)
    {
        _value = value;
        Errors = errors;
    }

    /// <summary>A snapshot: later changes to the originating notification or list never leak into it.</summary>
    public IReadOnlyList<Error> Errors { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    /// <summary>Whether any error breaks <paramref name="rule"/> (see <see cref="Error.Is"/>).</summary>
    public bool Has(Error rule) => Errors.Any(e => e.Is(rule));

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = IsSuccess ? _value : default;
        return IsSuccess;
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<IReadOnlyList<Error>, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);
        return IsSuccess ? onSuccess(_value!) : onFailure(Errors);
    }

    /// <summary>The same outcome without the value, for a caller that only reports success or failure.</summary>
    public Result WithoutValue() => IsSuccess ? Result.Success() : Result.Failure(Errors);

    public static implicit operator Result<T>(T value) => Result.Success(value);

    public static implicit operator Result<T>(Error error) => Result.Failure<T>(error);
}
