namespace Frappe.Kernel;

/// <summary>
/// Error accumulation and JSON Pointer support that ErrorOr does not provide.
/// </summary>
public static class Errors
{
    /// <summary>The <see cref="Error.Metadata"/> key that holds the JSON Pointer of the failing input.</summary>
    public const string PointerKey = "pointer";

    /// <summary>Collects every error of every result, so one response reports all of them.</summary>
    /// <param name="results">The results to check.</param>
    /// <returns><see cref="Result.Success"/>, or every error in argument order.</returns>
    public static ErrorOr<Success> Collect(params ReadOnlySpan<IErrorOr> results)
    {
        List<Error> errors = [];
        foreach (IErrorOr result in results)
        {
            ArgumentNullException.ThrowIfNull(result, nameof(results));
            if (result.IsError)
            {
                errors.AddRange(result.Errors ?? []);
            }
        }

        return errors.Count == 0 ? Result.Success : errors;
    }

    /// <summary>Records which input field every error of <paramref name="result"/> belongs to.</summary>
    /// <typeparam name="T">The result's value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="jsonPointer">An RFC 6901 JSON Pointer such as <c>/label</c>.</param>
    /// <returns>The value unchanged, or the same errors with the pointer in their metadata.</returns>
    /// <exception cref="ArgumentException"><paramref name="jsonPointer"/> does not start with '/'.</exception>
    public static ErrorOr<T> WithPointer<T>(this ErrorOr<T> result, string jsonPointer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPointer);
        if (!jsonPointer.StartsWith('/'))
        {
            throw new ArgumentException(
                $"'{jsonPointer}' is not a JSON Pointer: it must start with '/'.",
                nameof(jsonPointer)
            );
        }

        if (!result.IsError)
        {
            return result;
        }

        return result.Errors.ConvertAll(error => error.WithMetadata(PointerKey, jsonPointer));
    }

    private static Error WithMetadata(this Error error, string key, object value)
    {
        Dictionary<string, object> metadata = error.Metadata is null
            ? []
            : new(error.Metadata, StringComparer.Ordinal);
        metadata[key] = value;
        return Error.Custom(error.NumericType, error.Code, error.Description, metadata);
    }
}
