using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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

/// <summary>The failed case of a <see cref="Result{T}"/>: at least one <see cref="Error"/>.</summary>
public sealed class Failure
{
    public Failure(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed result needs at least one error.", nameof(errors));
        }

        Errors = errors;
    }

    public IReadOnlyList<Error> Errors { get; }
}

/// <summary>
/// The outcome of an operation that can fail for business reasons: either a value or at least one <see cref="Error"/>.
/// Business failures are values, not exceptions. Exceptions are reserved for bugs and infrastructure faults.
/// </summary>
/// <remarks>
/// A C# 15 union of two cases, the value (<typeparamref name="T"/>) and a <see cref="Failure"/>: a value or a failure
/// converts to it implicitly, and a <c>switch</c> over it is exhaustive once it handles both cases, for example
/// <c>result switch { Failure failure =&gt; ..., T value =&gt; ... }</c>. Patterns test the contents, so a successful
/// result whose value is <see langword="null"/> matches the <see langword="null"/> pattern. It is a class-based union
/// (<see cref="UnionAttribute"/> with a member provider) rather than a <c>union</c> declaration, which would be a struct
/// storing an <see langword="object"/>: a value is never boxed, and <see cref="Value"/> stays typed.
/// </remarks>
[Union]
public sealed class Result<T> : Result<T>.IUnionMembers
{
    private readonly T _value;
    private readonly Failure? _failure;

    private Result(T value)
    {
        _value = value;
    }

    private Result(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _value = default!;
        _failure = failure;
    }

    /// <summary>The union members the compiler uses: how each case enters the union, and how patterns read it back.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The union creation members the C# 15 union pattern requires.")]
    public interface IUnionMembers
    {
        static Result<T> Create(T value) => new(value);

        static Result<T> Create(Failure value) => new(value);

        object? Value { get; }

        bool HasValue { get; }

        bool TryGetValue([MaybeNullWhen(false)] out T value);

        bool TryGetValue([MaybeNullWhen(false)] out Failure value);
    }

    public bool IsSuccess => _failure is null;

    public IReadOnlyList<Error> Errors => _failure?.Errors ?? [];

    /// <summary>The value of a successful result. Reading it from a failure is a bug.</summary>
    public T Value => _failure is null
        ? _value
        : throw new InvalidOperationException($"A failed result has no value. First error: {_failure.Errors[0].Code}.");

    object? IUnionMembers.Value => _failure ?? (object?)_value;

    bool IUnionMembers.HasValue => _failure is not null || _value is not null;

    bool IUnionMembers.TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return _failure is null && _value is not null;
    }

    bool IUnionMembers.TryGetValue([MaybeNullWhen(false)] out Failure value)
    {
        value = _failure;
        return _failure is not null;
    }

#pragma warning disable CA2225 // Result.Failure is the named alternative.
    public static implicit operator Result<T>(Error error) => new(new Failure([error]));
#pragma warning restore CA2225
}
