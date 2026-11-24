using Frappe.Access.Contracts;

namespace Frappe.Access.Application;

/// <summary>What one request declared about who may run it, read once from its type.</summary>
/// <param name="Name">The request's type name: the tag and the log field.</param>
/// <param name="Rule">The rule of a guarded request; nothing for a public or undeclared one.</param>
/// <param name="IsPublic">The request carries <see cref="IPublic"/> and no rule.</param>
/// <param name="ActsOn">The resource types the handler must check (<see cref="IActsOn{TResource}"/>).</param>
internal sealed record UseCaseDeclaration(string Name, CallerRule? Rule, bool IsPublic, IReadOnlyCollection<Type> ActsOn);
