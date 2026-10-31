using Frappe.Geo.Application;
using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Platform.Http.Paging;
using Frappe.Platform.Http.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.Geo.Infrastructure.Currencies.ListCurrencies;

internal static class ListCurrenciesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group
            .MapGet("/currencies", List)
            .AllowAnonymous()
            .WithName("ListCurrencies")
            .WithSummary(
                "Lists the ISO 4217 currencies in use, by their name in the requested locale."
            )
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static Results<Ok<PageResponse<CurrencyDto>>, ProblemHttpResult> List(
        string? locale,
        ICurrencyReader currencies,
        HttpContext context
    )
    {
        ErrorOr<Locale> requested = BodyLocale.Of(locale);
        if (requested.IsError)
        {
            return requested.Errors.ToProblem(context);
        }

        Localized<IReadOnlyList<CurrencyDto>> list = currencies.List(requested.Value);
        BodyLocale.Served(context, list.Locale);
        return TypedResults.Ok(new PageResponse<CurrencyDto>(list.Value, NextCursor: null));
    }
}
