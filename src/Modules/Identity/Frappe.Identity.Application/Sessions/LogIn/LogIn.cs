using Frappe.Application.Abstractions.Messaging;
using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Signs an owner in with email and password from <paramref name="ClientIp"/>, on <paramref name="Surface"/>. Like
/// every command, the client's raw fields arrive as sent (possibly null) and the handler decides what they mean.
/// </summary>
public sealed record LogIn(string? Email, string? Password, string ClientIp, SessionSurface Surface) : ICommand;
