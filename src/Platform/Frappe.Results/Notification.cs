namespace Frappe.Results;

/// <summary>
/// Collects every error of a validation pass instead of stopping at the first one
/// (Martin Fowler, "Replacing Throwing Exceptions with Notification in Validations").
/// </summary>
/// <example>
/// <code>
/// var notification = new Notification();
/// var country = notification.Collect(CountryCode.Create(rawCountry));
/// var year = notification.Collect(HolidayYear.Create(rawYear));
/// if (notification.HasErrors) return notification.ToFailure&lt;Holidays&gt;();
/// </code>
/// </example>
public sealed class Notification
{
    private readonly List<Error> _errors = [];

    public IReadOnlyList<Error> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public void Add(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _errors.Add(error);
    }

    /// <summary>Returns the value of a successful result, or records its errors and returns <c>default</c>.</summary>
    public T? Collect<T>(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        _errors.AddRange(result.Errors);
        return default;
    }

    /// <summary>Turns the collected errors into a failed result. Calling it without errors is a bug.</summary>
    public Result<T> ToFailure<T>() => HasErrors
        ? Result.Failure<T>([.. _errors])
        : throw new InvalidOperationException("The notification has no errors to report.");
}
