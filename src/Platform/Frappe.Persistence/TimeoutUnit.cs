namespace Frappe.Persistence;

/// <summary>The unit a timeout must be a whole number of: Npgsql reads whole seconds, Postgres whole milliseconds.</summary>
internal enum TimeoutUnit
{
    /// <summary>Whole seconds.</summary>
    Seconds,

    /// <summary>Whole milliseconds.</summary>
    Milliseconds,
}
