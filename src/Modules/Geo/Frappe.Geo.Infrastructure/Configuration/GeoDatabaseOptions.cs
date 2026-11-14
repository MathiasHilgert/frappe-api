namespace Frappe.Geo.Infrastructure.Configuration;

/// <summary>Geo's database: <see cref="ConnectionString"/> from <c>ConnectionStrings:Geo</c>, with the module's own role.</summary>
public sealed class GeoDatabaseOptions
{
    /// <summary>The name of Geo's connection string, <c>ConnectionStrings:Geo</c>.</summary>
    public const string ConnectionStringName = "Geo";

    /// <summary>The connection string of Geo's database.</summary>
    public string? ConnectionString { get; set; }
}
