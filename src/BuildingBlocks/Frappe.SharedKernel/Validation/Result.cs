using System.Diagnostics.CodeAnalysis;

namespace Frappe.SharedKernel.Validation;

public class Result
{
    protected Result(IReadOnlyList<Error> errors) => Errors = errors;

    /// <summary>A snapshot: later changes to the originating notification never leak into it.</summary>
    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new([]);
    public static Result<T> Success<T>(T value) => new(value, []);

    public static Result Failure(ValidationNotification notification) => new(Snapshot(notification));
    public static Result Failure(Error error) => new([error]);
    public static Result<T> Failure<T>(ValidationNotification notification) => new(default, Snapshot(notification));
    public static Result<T> Failure<T>(Error error) => new(default, [error]);

    /// <summary>Validates every rule into one notification and builds the value only when none broke.</summary>
    public static Result<T> From<T>(ValidationNotification notification, Func<T> create) =>
        notification.HasErrors ? Failure<T>(notification) : Success(create());

    private static Error[] Snapshot(ValidationNotification notification) =>
        notification.HasErrors
            ? [.. notification.Errors]
            : throw new ArgumentException("A failure needs at least one error.", nameof(notification));
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, IReadOnlyList<Error> errors) : base(errors) => _value = value;

    /// <summary>Same as the base flag, restated so the compiler knows <see cref="Value"/> is set when it is true.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    public new bool IsSuccess => base.IsSuccess;

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

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}
