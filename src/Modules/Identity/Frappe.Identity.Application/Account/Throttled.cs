using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Account;

/// <summary>A result that may never have been attempted because the caller hit an attempt limit.</summary>
public sealed record Throttled<T>(Result<T>? Result, TimeSpan RetryAfter)
{
    public Throttled(Result<T> result) : this(result, TimeSpan.Zero)
    {
    }
}
