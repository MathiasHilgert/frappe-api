using System.Diagnostics;

namespace Frappe.Architecture.Tests;

/// <summary>
/// Deliberate violations the architecture tests must see: a type that names a use case, the web stack and a tracing source, and a port that
/// saves. Nothing in production uses them; they prove each rule can fail.
/// </summary>
public static class RuleBreakers
{
    /// <summary>Names <c>Frappe.UseCases</c> (ARCH-03).</summary>
    public static Frappe.UseCases.ICommand<int>? UseCase => null;

    /// <summary>Names the web stack (ARCH-04).</summary>
    public static Microsoft.AspNetCore.Http.HttpContext? Web => null;

    /// <summary>Names storage (ARCH-04, Http side).</summary>
    public static Microsoft.EntityFrameworkCore.DbContext? Storage => null;

    /// <summary>Names a tracing source (ARCH-12).</summary>
    public static ActivitySource? Tracing => null;
}
