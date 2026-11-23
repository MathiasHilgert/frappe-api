namespace Frappe.Http;

/// <summary>The kind of value a parameter documented with <see cref="DocumentedParameterAttribute"/> accepts.</summary>
public enum ParameterFormat
{
    /// <summary>Any text.</summary>
    Text,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary>An ISO 8601 calendar date (<c>yyyy-MM-dd</c>).</summary>
    Date,

    /// <summary>A UUID (<c>5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10</c>).</summary>
    Uuid,
}
