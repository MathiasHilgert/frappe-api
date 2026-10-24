namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// Marks a message that only reads. Its handler asks a read port for a no-tracking projection and answers
/// <c>Result&lt;TResult&gt;</c>, like a command, so every caller unwraps one shape; it never loads an aggregate to change
/// it and never saves.
/// </summary>
/// <typeparam name="TResult">What a successful query answers with.</typeparam>
#pragma warning disable S2326 // The type parameter declares the answer and lets the architecture tests find read models.
public interface IQuery<TResult>;
#pragma warning restore S2326
