using Frappe.Geo.Application;
using Frappe.Geo.Application.Locales;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Locales.ListLocales;

internal static class ListLocalesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/locales", List)
            .AllowAnonymous()
            .WithName("ListLocales")
            .WithSummary(
                "Lists the CLDR locales with modern coverage, by their name in the requested locale."
            )
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<LocaleDto>>, ProblemHttpResult> List(
        string? locale,
        ILocaleReader locales,
        HttpContext context
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        if (requested.IsError)
        {
            return requested.Errors.ToProblem(context);
        }

        Localized<IReadOnlyList<LocaleDto>> list = locales.List(requested.Value);
        BodyLocale.Served(context, list.Locale);
        return TypedResults.Ok(new PageResponse<LocaleDto>(list.Value, NextCursor: null));
    }
}
