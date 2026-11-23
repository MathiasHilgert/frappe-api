namespace Frappe.Results;

/// <summary>Factory methods for <see cref="Result{T}"/>, and its composition: <c>Then</c>, <c>Map</c> and <c>Ensure</c>, sync and async.</summary>
public static class Result
{
    extension<T>(Result<T> result)
    {
        /// <summary>Runs <paramref name="next"/> on the value of a success; a failure short-circuits with its errors unchanged.</summary>
        public Result<TNext> Then<TNext>(Func<T, Result<TNext>> next)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(next);
            return result.IsSuccess ? next(result.Value) : Failure<TNext>(result.Errors);
        }

        /// <summary>Runs the async <paramref name="next"/> on the value of a success; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Then<TNext>(Func<T, Task<Result<TNext>>> next)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(next);
            return result.IsSuccess ? await next(result.Value) : Failure<TNext>(result.Errors);
        }

        /// <summary>Transforms the value of a success; a failure short-circuits with its errors unchanged.</summary>
        public Result<TNext> Map<TNext>(Func<T, TNext> map)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(map);
            return result.IsSuccess ? Success(map(result.Value)) : Failure<TNext>(result.Errors);
        }

        /// <summary>Transforms the value of a success with the async <paramref name="map"/>; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Map<TNext>(Func<T, Task<TNext>> map)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(map);
            return result.IsSuccess ? Success(await map(result.Value)) : Failure<TNext>(result.Errors);
        }

        /// <summary>
        /// Turns a success into <paramref name="error"/> when <paramref name="predicate"/> rejects its value. A failure is
        /// kept as it is: the existing errors are never replaced or extended.
        /// </summary>
        public Result<T> Ensure(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(predicate);
            ArgumentNullException.ThrowIfNull(error);
            return result.IsSuccess && !predicate(result.Value) ? error : result;
        }
    }

    extension<T>(Task<Result<T>> pending)
    {
        /// <summary>Awaits the result, then runs <c>next</c> on the value of a success; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Then<TNext>(Func<T, Result<TNext>> next)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return (await pending).Then(next);
        }

        /// <summary>Awaits the result, then runs <c>next</c> on the value of a success; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Then<TNext>(Func<T, Task<Result<TNext>>> next)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return await (await pending).Then(next);
        }

        /// <summary>Awaits the result, then transforms the value of a success; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Map<TNext>(Func<T, TNext> map)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return (await pending).Map(map);
        }

        /// <summary>Awaits the result, then transforms the value of a success; a failure keeps its errors unchanged.</summary>
        public async Task<Result<TNext>> Map<TNext>(Func<T, Task<TNext>> map)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return await (await pending).Map(map);
        }

        /// <summary>Awaits the result, then turns a success rejected by the predicate into the error; a failure is kept as it is.</summary>
        public async Task<Result<T>> Ensure(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(pending);
            return (await pending).Ensure(predicate, error);
        }
    }

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
