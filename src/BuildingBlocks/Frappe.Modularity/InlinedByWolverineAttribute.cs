namespace Frappe.Modularity;

/// <summary>
/// Marks an Infrastructure type that stays public only because Wolverine builds handler dependencies inline in the
/// generated code when their concrete type is public (a DbContext, a repository, a read model). Everything else in a
/// module's Infrastructure is internal; the architecture tests allow exactly the marked types and the module.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InlinedByWolverineAttribute : Attribute;
