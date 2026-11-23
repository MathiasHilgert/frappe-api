namespace Frappe.Persistence;

/// <summary>
/// The database facet of a module: what the Migrator needs from it, declared apart from its services
/// (<c>IModule</c>) and its endpoints, which are independent interfaces. A host lists the facets it uses in one place.
/// </summary>
public interface IDatabaseModule : IDatabaseParticipant;
