using Frappe.Lexicon.Application;
using Frappe.Platform.Http.Problems;
using Frappe.Platform.Http.RateLimiting;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Frappe.Lexicon.Infrastructure;

/// <summary>A fake module whose error texts live in its Application assembly.</summary>
public sealed class LexiconModule : IModule
{
    /// <inheritdoc />
    public ModuleMetadata Metadata { get; } =
        new("lexicon", "lexicon", "lexicons", new EventIdRange(3000, 3999), [], [], []);

    /// <inheritdoc />
    public void AddServices(IHostApplicationBuilder builder) { }

    /// <inheritdoc />
    public void MapEndpoints(RouteGroupBuilder group) =>
        group
            .MapGet(
                "/words/{word}",
                static (string word, HttpContext context) =>
                    new[] { LexiconErrors.UnknownWord }.ToProblem(context)
            )
            .WithName("GetWord")
            .WithSummary("Answers that the word is unknown.")
            .RequireRateLimiting(RateLimitPolicies.Public);
}
