namespace Frappe.Http;

/// <summary>The limits every paged list shares (HTTP-14), so descriptions and validation state the same numbers.</summary>
public static class PageLimits
{
    /// <summary>The page size when the request names none.</summary>
    public const int Default = 50;

    /// <summary>The smallest page a request may ask for.</summary>
    public const int Min = 1;

    /// <summary>The largest page a request may ask for.</summary>
    public const int Max = 200;

    /// <summary>The error codes of a page request, texts in the platform messages.</summary>
    public const string LimitOutOfRangeCode = "platform.page_limit.out_of_range";

    /// <inheritdoc cref="LimitOutOfRangeCode"/>
    public const string LimitInvalidCode = "platform.page_limit.invalid";

    /// <inheritdoc cref="LimitOutOfRangeCode"/>
    public const string CursorInvalidCode = "platform.page_cursor.invalid";
}
