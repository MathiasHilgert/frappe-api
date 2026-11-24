namespace Frappe.Access.Contracts;

/// <summary>
/// The rules of one module's resources: one implementation per owning module, registered in its container, which declares each resource in
/// the <see cref="RuleBook"/> Access builds at startup. It lives in the module that owns the resource (S3), so a module adds its own
/// rules without touching Access.
/// </summary>
public interface IRules
{
    /// <summary>Declares the module's resources in <paramref name="book"/>.</summary>
    void Define(RuleBook book);
}
