namespace Frappe.Results;

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
