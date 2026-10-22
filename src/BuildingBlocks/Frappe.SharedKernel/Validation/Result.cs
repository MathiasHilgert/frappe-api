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

    public static Result Failure(Notification notification) => new(Snapshot(notification));
    public static Result Failure(Error error) => new([error]);
    public static Result<T> Failure<T>(Notification notification) => new(default, Snapshot(notification));
    public static Result<T> Failure<T>(Error error) => new(default, [error]);

    /// <summary>Validates every rule into one notification and builds the value only when none broke.</summary>
    public static Result<T> From<T>(Notification notification, Func<T> create) =>
        notification.HasErrors ? Failure<T>(notification) : Success(create());

    private static Error[] Snapshot(Notification notification) =>
        notification.HasErrors
            ? [.. notification.Errors]
            : throw new ArgumentException("A failure needs at least one error.", nameof(notification));
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, IReadOnlyList<Error> errors) : base(errors) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result.");
}
