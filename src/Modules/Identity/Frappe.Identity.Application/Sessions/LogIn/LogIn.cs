using Frappe.Application.Abstractions.Messaging;
using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Signs an owner in with email and password (S5) from <paramref name="ClientIp"/>, on <paramref name="Surface"/>.
/// </summary>
public sealed record LogIn(string Email, string Password, string ClientIp, SessionSurface Surface) : ICommand;
