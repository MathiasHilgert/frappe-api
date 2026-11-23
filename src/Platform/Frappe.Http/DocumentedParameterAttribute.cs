namespace Frappe.Http;

/// <summary>
/// What a query parameter accepts, for the API document. Endpoints bind query values as text on purpose, so the use
/// case validates them and answers its own stable, translated errors instead of the framework's generic
/// <c>http.bad_request</c>; the C# type then says nothing about the value, and this attribute says it instead: whether
/// it is required, its format, its allowed values or pattern and its bounds. It changes nothing at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DocumentedParameterAttribute : Attribute
{
    /// <summary>The value of <see cref="Minimum"/> and <see cref="Maximum"/> when there is no bound.</summary>
    public const int Unbounded = int.MinValue;

    /// <summary>The request is invalid without it.</summary>
    public bool Required { get; init; }

    /// <summary>The kind of value; <see cref="ParameterFormat.Text"/> by default.</summary>
    public ParameterFormat Format { get; init; }

    /// <summary>The only values accepted, when they are a closed list.</summary>
    public string[]? Values { get; init; }

    /// <summary>A regular expression every accepted value matches (ECMA-262), when the values are an open set.</summary>
    public string? Pattern { get; init; }

    /// <summary>The smallest <see cref="ParameterFormat.WholeNumber"/> accepted, or <see cref="Unbounded"/>.</summary>
    public int Minimum { get; init; } = Unbounded;

    /// <summary>The largest <see cref="ParameterFormat.WholeNumber"/> accepted, or <see cref="Unbounded"/>.</summary>
    public int Maximum { get; init; } = Unbounded;
}
