using Frappe.Platform.Http.Localization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.UnitTests.Http;

public sealed class RequestLocaleTests
{
    [Fact]
    public void The_locale_query_wins_over_accept_language()
    {
        HttpRequest request = Request("?locale=es", "en");

        RequestLocale.Of(request).Value.ShouldBe("es");
    }

    [Fact]
    public void An_invalid_locale_query_falls_back_to_accept_language()
    {
        HttpRequest request = Request("?locale=not_a_tag!", "pt-BR");

        RequestLocale.Of(request).Value.ShouldBe("pt-BR");
    }

    [Fact]
    public void Accept_language_is_read_by_quality_skipping_invalid_tags()
    {
        HttpRequest request = Request("", "*, en;q=0.5, es-ar;q=0.9");

        RequestLocale.Of(request).Value.ShouldBe("es-AR");
    }

    // q=0 means "not acceptable" (RFC 9110), so a client that refuses Spanish never reads it.
    [Fact]
    public void An_accept_language_tag_with_zero_quality_is_never_chosen()
    {
        HttpRequest request = Request("", "es;q=0");

        RequestLocale.Of(request).Value.ShouldBe("en");
    }

    // A hostile header may carry thousands of tags; only the first three are tried, as ASP.NET Core does.
    [Fact]
    public void Only_the_first_three_accept_language_values_are_tried()
    {
        HttpRequest request = Request("", "x!, y!, z!, es");

        RequestLocale.Of(request).Value.ShouldBe("en");
    }

    [Fact]
    public void The_third_accept_language_value_is_still_tried()
    {
        HttpRequest request = Request("", "x!, y!, es");

        RequestLocale.Of(request).Value.ShouldBe("es");
    }

    [Fact]
    public void Without_a_query_or_header_the_locale_is_english()
    {
        HttpRequest request = Request("", null);

        RequestLocale.Of(request).Value.ShouldBe("en");
    }

    private static HttpRequest Request(string query, string? acceptLanguage)
    {
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString(query);
        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        return context.Request;
    }
}
