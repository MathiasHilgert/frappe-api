using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Problems;

/// <summary>A temporarily exhausted capacity (<see cref="UnavailableException"/>) answers the one problem shape, 503.</summary>
internal sealed class UnavailableExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not UnavailableException unavailable)
            return false;

        await Result.Failure(unavailable.Error).ToProblem().ExecuteAsync(httpContext);
        return true;
    }
}
