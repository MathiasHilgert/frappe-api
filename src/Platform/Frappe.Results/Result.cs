namespace Frappe.Results;

/// <summary>Factory methods for <see cref="Result{T}"/>.</summary>
public static class Result
{
    public static Result<T> Success<T>(T value) => value;

    public static Result<T> Failure<T>(IReadOnlyList<Error> errors) => new Frappe.Results.Failure(errors);

    /// <summary>
    /// Validates several inputs at once: either all their values together, or every error of every input
    /// (the applicative counterpart of <see cref="Notification"/>, for a fixed set of values).
    /// </summary>
    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> first, Result<T2> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return first.IsSuccess && second.IsSuccess
            ? (first.Value, second.Value)
            : Failure<(T1, T2)>([.. first.Errors, .. second.Errors]);
    }

    /// <inheritdoc cref="Combine{T1, T2}(Result{T1}, Result{T2})"/>
    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> first, Result<T2> second, Result<T3> third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        return first.IsSuccess && second.IsSuccess && third.IsSuccess
            ? (first.Value, second.Value, third.Value)
            : Failure<(T1, T2, T3)>([.. first.Errors, .. second.Errors, .. third.Errors]);
    }
}
