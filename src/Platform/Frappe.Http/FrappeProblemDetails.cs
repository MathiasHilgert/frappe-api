using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Frappe.Http.ResultHttpExtensions;

namespace Frappe.Http;

/// <summary>
/// Every Problem Details response of Frappé is written by the framework's <see cref="IProblemDetailsService"/> and
/// shaped here, in <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/>, with one contract (RFC 9457):
/// <list type="bullet">
/// <item><c>type</c> is always the <see cref="ProblemCategory"/> (<c>urn:frappe:problem:not-found</c>), never an error code;</item>
/// <item><c>title</c> is the category's text and <c>detail</c> the error's text, or how many errors there are when there are several, both in the negotiated language;</item>
/// <item><c>errors</c> lists every error with its <c>code</c>, its <c>parameters</c> and its <c>message</c>;</item>
/// <item><c>instance</c> is the request path and <c>traceId</c> the W3C trace id.</item>
/// </list>
/// The framework's English defaults (<c>title</c>, an RFC link as <c>type</c>) are always replaced: a text the catalog
/// does not have is left out instead. A problem describes one request only, so no cache may keep it (RFC 9111
/// §5.2.2.5), and <c>Content-Language</c> names every language its texts are written in.
/// </summary>
internal static class FrappeProblemDetails
{
    private static readonly IReadOnlyDictionary<string, object?> NoParameters = new Dictionary<string, object?>();

    /// <summary>
    /// Registers the framework's problem service with the Frappé contract and <see cref="FrappeProblemDetailsWriter"/> ahead of the
    /// framework's own writer, so every problem, whoever raises it, is written by Frappé.
    /// </summary>
    public static IServiceCollection AddFrappeProblemDetails(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProblemDetailsWriter, FrappeProblemDetailsWriter>());
        return services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);
    }

    /// <summary>Answers <paramref name="occurrence"/> as a problem through <see cref="IProblemDetailsService"/>.</summary>
    public static Task WriteAsync(HttpContext httpContext, ProblemOccurrence occurrence)
    {
        httpContext.Features.Set(occurrence);
        httpContext.Response.StatusCode = occurrence.Status;
        return httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = occurrence.Status },
        }).AsTask();
    }

    /// <summary>
    /// Shapes the problem from the request's <see cref="ProblemOccurrence"/>; a problem the framework writes on its
    /// own, without one, is told from its status like the platform's (<see cref="PlatformProblem.ForStatus"/>), in the
    /// negotiated language.
    /// </summary>
    internal static void Customize(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        var problem = context.ProblemDetails;
        var occurrence = httpContext.Features.Get<ProblemOccurrence>()
            ?? PlatformProblem.ForStatus(httpContext, problem.Status ?? httpContext.Response.StatusCode);

        // The framework writes some problems itself (a JSON body it cannot read) before any endpoint filter negotiated
        // the language: negotiate here too, once, so they follow Accept-Language like every other problem.
        LanguageNegotiation.Apply(httpContext);
        var text = TextRenderer.For(httpContext);

        // The title first: when an error falls back to another language, Content-Language names the title's first.
        problem.Type = occurrence.Category.Type;
        problem.Title = text.Render(occurrence.Category.TitleKey, NoParameters);
        problem.Status = occurrence.Status;
        var errors = occurrence.Errors.Select(error => error with { Message = text.Render(error.Code, error.Parameters) }).ToArray();
        problem.Detail = errors.Length == 1
            ? errors[0].Message
            : text.Render(ProblemTexts.Several, new Dictionary<string, object?> { ["count"] = errors.Length });
        problem.Instance = httpContext.Request.Path.Value;

        // A fresh map keeps the members in this order whatever the framework added first; parameters always travel
        // inside each error, so they can never collide with a problem member.
        problem.Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["errors"] = errors,
            ["traceId"] = problem.Extensions.TryGetValue("traceId", out var traceId) && traceId is not null
                ? traceId
                : Activity.Current?.Id ?? httpContext.TraceIdentifier,
        };

        text.DeclareLanguages(httpContext.Response);
        httpContext.Response.Headers.CacheControl = "no-store";
    }
}
