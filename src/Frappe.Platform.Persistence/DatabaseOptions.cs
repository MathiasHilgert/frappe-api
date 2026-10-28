using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Persistence;

internal sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required]
    [NpgsqlConnectionString]
    public string Frappe { get; init; } = "";
}
