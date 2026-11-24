using System.Diagnostics.CodeAnalysis;

namespace Frappe.Access.Contracts;

/// <summary>
/// An action on a resource of type <typeparamref name="T"/>, named once next to the resource (<c>new("revoke")</c>). The type parameter ties
/// it to its book, so an action of one resource cannot be checked against another.
/// </summary>
/// <param name="Name">The action's stable name, unique inside its resource: it is how the book finds the rule.</param>
[SuppressMessage("Major Code Smell", "S2326", Justification = "CRAFT-10: the type parameter binds the action to its resource at compile time.")]
public sealed record ResourceAction<T>(string Name)
    where T : class;
