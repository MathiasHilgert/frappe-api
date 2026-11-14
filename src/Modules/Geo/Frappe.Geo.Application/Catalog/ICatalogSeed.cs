namespace Frappe.Geo.Application.Catalog;

/// <summary>
/// The reference catalog a deployment starts from (countries, currencies and their names), as a desired state. The
/// host that migrates the database chooses the seed; Geo's core never knows where it comes from.
/// </summary>
public interface ICatalogSeed
{
    CatalogSnapshot Load();
}
