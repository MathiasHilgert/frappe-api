using System.Text.Json;
using Alba;

namespace Frappe.Geo.IntegrationTests;

public sealed class CityTests(GeoFixture geo)
{
    [Fact]
    public async Task A_city_carries_its_id_names_province_label_and_zone()
    {
        JsonElement city = (await SearchAsync("villa maria cordoba"))[0];

        city.GetProperty("cityId").GetString().ShouldBe("019b76da-a800-7000-8000-000000d6443a");
        city.GetProperty("name").GetString().ShouldBe("Villa María");
        city.GetProperty("countryCode").GetString().ShouldBe("AR");
        city.GetProperty("subdivisionCode").GetString().ShouldBe("AR-X");
        city.GetProperty("subdivisionName").GetString().ShouldBe("Córdoba");
        city.GetProperty("label").GetString().ShouldBe("Villa María, Córdoba, Argentina");
        city.GetProperty("timeZoneId").GetString().ShouldBe("America/Argentina/Cordoba");
    }

    [Fact]
    public async Task An_exact_name_comes_before_name_prefixes()
    {
        JsonElement[] cities = await SearchAsync("Pila");

        Describe(cities[0]).ShouldBe("Pila|AR-B");
        cities
            .Skip(1)
            .Select(Describe)
            .Contains("Pilar|AR-B", StringComparer.Ordinal)
            .ShouldBeTrue();
        cities
            .Skip(1)
            .Select(Describe)
            .Contains("Pilar|AR-X", StringComparer.Ordinal)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_whole_name_finds_the_locality_first()
    {
        JsonElement[] cities = await SearchAsync("villa maria");

        cities[0].GetProperty("name").GetString().ShouldBe("Villa María");
    }

    [Fact]
    public async Task A_province_word_narrows_the_localities_to_that_province()
    {
        JsonElement[] cities = await SearchAsync("pilar cordoba");

        Describe(cities[0]).ShouldBe("Pilar|AR-X");
        cities[0].GetProperty("label").GetString().ShouldBe("Pilar, Córdoba, Argentina");
        cities.Select(Describe).Contains("Pilar|AR-B", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Name_province_and_country_words_find_a_locality()
    {
        Describe((await SearchAsync("villa maria cordoba"))[0]).ShouldBe("Villa María|AR-X");
        Describe((await SearchAsync("Pilar, Buenos Aires, Argentina"))[0]).ShouldBe("Pilar|AR-B");
    }

    // A label typed back with its commas reaches the exact and prefix buckets like the bare words do.
    [Fact]
    public async Task A_typed_back_label_ranks_its_locality_first()
    {
        Describe((await SearchAsync(Uri.EscapeDataString("Pilar, Buenos Aires")))[0])
            .ShouldBe("Pilar|AR-B");
    }

    [Theory]
    [InlineData("pilar")]
    [InlineData("pi")]
    public async Task A_trailing_comma_does_not_change_the_result(string q)
    {
        string[] bare = [.. (await SearchAsync(q)).Select(Describe)];
        string[] comma = [.. (await SearchAsync(Uri.EscapeDataString(q + ","))).Select(Describe)];

        comma.ShouldBe(bare);
    }

    [Fact]
    public async Task Accents_and_case_do_not_change_the_result()
    {
        string[] folded = [.. (await SearchAsync("cordoba")).Select(Describe)];
        string[] accented = [.. (await SearchAsync("Córdoba")).Select(Describe)];

        accented.ShouldBe(folded);
    }

    [Fact]
    public async Task Cordoba_ranks_the_city_then_names_with_cordoba_then_the_province()
    {
        JsonElement[] cities = await SearchAsync("cordoba");

        cities.Length.ShouldBe(10);
        Describe(cities[0]).ShouldBe("Córdoba|AR-X");
        Describe(cities[1]).ShouldBe("Villa Córdoba|AR-W");
        cities[2..].ShouldAllBe(city => city.GetProperty("subdivisionCode").GetString() == "AR-X");
    }

    [Fact]
    public async Task A_misspelling_of_three_or_more_characters_finds_similar_names()
    {
        string[] cities = [.. (await SearchAsync("pilr")).Select(Describe)];

        cities.Contains("Pilar|AR-X", StringComparer.Ordinal).ShouldBeTrue();
        cities.Contains("Pilar|AR-B", StringComparer.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task A_misspelled_name_with_its_province_ranks_that_locality_first()
    {
        Describe((await SearchAsync("pilr cordoba"))[0]).ShouldBe("Pilar|AR-X");
    }

    [Fact]
    public async Task Rio_cordoba_ranks_the_cordoba_localities_named_rio_first()
    {
        JsonElement[] cities = await SearchAsync("rio cordoba");

        cities.Length.ShouldBe(10);
        cities.ShouldAllBe(city =>
            city.GetProperty("subdivisionCode").GetString() == "AR-X"
            && Application
                .Cities.SearchText.Fold(city.GetProperty("name").GetString() ?? "")
                .Contains("rio")
        );
    }

    [Fact]
    public async Task Short_words_match_only_at_word_starts()
    {
        JsonElement[] cities = await SearchAsync("v m");
        string[] villas =
        [
            .. (await SearchAsync("villa ma&subdivisionCode=AR-X")).Select(Describe),
        ];

        cities.ShouldNotBeEmpty();
        cities.ShouldAllBe(city => HasWordStarting(city, "v") && HasWordStarting(city, "m"));
        villas.Contains("Villa María|AR-X", StringComparer.Ordinal).ShouldBeTrue();
    }

    [Theory]
    [InlineData("50%")]
    [InlineData("a_b")]
    public async Task Wildcards_in_a_query_match_literally(string q)
    {
        (await SearchAsync(Uri.EscapeDataString(q))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_search_returns_at_most_ten_cities_and_no_cursor()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?q=san&limit=50",
            200
        );

        page.GetProperty("items").GetArrayLength().ShouldBe(10);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_locale_changes_the_province_and_country_names_of_the_label()
    {
        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?q=general%20alvear&subdivisionCode=AR-M&locale=es-AR",
            200
        );

        page.GetProperty("items")[0]
            .GetProperty("label")
            .GetString()
            .ShouldBe("General Alvear, Mendoza, Argentina");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("es");
        (_, JsonElement english) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?q=ushuaia&locale=fr",
            200
        );
        english
            .GetProperty("items")[0]
            .GetProperty("subdivisionName")
            .GetString()
            .ShouldBe("Tierra del Fuego");
    }

    [Fact]
    public async Task Same_name_cities_of_a_province_carry_their_department_and_unique_ones_do_not()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?q=san%20pedro&subdivisionCode=AR-G",
            200
        );

        string[] labels =
        [
            .. page.GetProperty("items")
                .EnumerateArray()
                .Select(static city => city.GetProperty("label").GetString() ?? ""),
        ];
        labels
            .Contains("San Pedro (Choya), Santiago del Estero, Argentina", StringComparer.Ordinal)
            .ShouldBeTrue();
        labels
            .Contains("San Pedro (Capital), Santiago del Estero, Argentina", StringComparer.Ordinal)
            .ShouldBeTrue();
        (await SearchAsync("villa maria cordoba"))[0]
            .GetProperty("label")
            .GetString()
            .ShouldBe("Villa María, Córdoba, Argentina");
    }

    [Fact]
    public async Task A_search_never_returns_two_items_with_the_same_label()
    {
        string[] labels =
        [
            .. (await SearchAsync("avellaneda")).Select(static city =>
                city.GetProperty("label").GetString() ?? ""
            ),
        ];

        labels.ShouldNotBeEmpty();
        labels.Distinct(StringComparer.Ordinal).Count().ShouldBe(labels.Length);
        // Its entity copy is gone, so the census locality is unique in its province and carries no department.
        labels
            .Contains("Avellaneda, Buenos Aires, Argentina", StringComparer.Ordinal)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("p")]
    [InlineData("%20%20")]
    public async Task A_query_under_two_folded_characters_is_rejected_at_q(string q)
    {
        JsonElement error = await ProblemAsync($"/v1/geo/cities?q={q}", 422);

        error.GetProperty("code").GetString().ShouldBe("geo.city.query_invalid");
        error.GetProperty("pointer").GetString().ShouldBe("/q");
    }

    [Fact]
    public async Task A_query_over_one_hundred_characters_is_rejected_at_q()
    {
        JsonElement error = await ProblemAsync($"/v1/geo/cities?q={new string('a', 101)}", 422);

        error.GetProperty("code").GetString().ShouldBe("geo.city.query_invalid");
        error.GetProperty("pointer").GetString().ShouldBe("/q");
    }

    [Fact]
    public async Task A_query_with_a_cursor_is_rejected_at_cursor()
    {
        JsonElement error = await ProblemAsync("/v1/geo/cities?q=pilar&cursor=abc", 422);

        error.GetProperty("code").GetString().ShouldBe("geo.city.query_invalid");
        error.GetProperty("pointer").GetString().ShouldBe("/cursor");
    }

    [Theory]
    [InlineData("subdivisionCode=AR-ZZ", "geo.subdivision.not_found", "/subdivisionCode")]
    [InlineData("countryCode=ZZ", "geo.country.not_found", "/countryCode")]
    [InlineData("subdivisionCode=nope", "geo.subdivision_code.invalid", "/subdivisionCode")]
    [InlineData("countryCode=A", "geo.country_code.invalid", "/countryCode")]
    public async Task A_bad_filter_is_rejected_at_its_parameter(
        string filter,
        string code,
        string errorPointer
    )
    {
        JsonElement error = await ProblemAsync($"/v1/geo/cities?{filter}", 422);

        error.GetProperty("code").GetString().ShouldBe(code);
        error.GetProperty("pointer").GetString().ShouldBe(errorPointer);
    }

    [Fact]
    public async Task Without_a_query_or_filter_cities_are_paged_by_folded_name()
    {
        (IScenarioResult result, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?limit=3",
            200
        );

        page.GetProperty("items").GetArrayLength().ShouldBe(3);
        page.GetProperty("nextCursor").GetString().ShouldNotBeNullOrEmpty();
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=86400");
    }

    [Fact]
    public async Task Walking_every_page_of_a_province_returns_each_city_once_in_name_order()
    {
        List<string> ids = [];
        List<string> names = [];
        string url = "/v1/geo/cities?subdivisionCode=AR-X&limit=200";
        string? cursor = null;
        do
        {
            (_, JsonElement page) = await GeoHttp.GetAsync(
                geo.Host,
                cursor is null ? url : $"{url}&cursor={cursor}",
                200
            );
            foreach (JsonElement city in page.GetProperty("items").EnumerateArray())
            {
                ids.Add(city.GetProperty("cityId").GetString() ?? "");
                names.Add(
                    Application.Cities.SearchText.Fold(city.GetProperty("name").GetString() ?? "")
                );
            }

            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        ids.Count.ShouldBeGreaterThan(400);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
        names.ShouldBe(names.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task A_cursor_reused_under_another_filter_is_a_bad_request()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?subdivisionCode=AR-X&limit=1",
            200
        );
        string cursor = page.GetProperty("nextCursor").GetString() ?? "";

        JsonElement error = await ProblemAsync(
            $"/v1/geo/cities?subdivisionCode=AR-B&limit=1&cursor={cursor}",
            400
        );

        error.GetProperty("code").GetString().ShouldBe("platform.paging.invalid_cursor");
        error.GetProperty("pointer").GetString().ShouldBe("/cursor");
    }

    // A word of the label (name, province, country) starts with the prefix, so the match is not an inner substring.
    private static bool HasWordStarting(JsonElement city, string prefix) =>
        Application
            .Cities.SearchText.Fold(city.GetProperty("label").GetString() ?? "")
            .Split([' ', ',', '(', ')', '.', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(prefix, StringComparison.Ordinal));

    private static string Describe(JsonElement city) =>
        $"{city.GetProperty("name").GetString()}|{city.GetProperty("subdivisionCode").GetString()}";

    private async Task<JsonElement[]> SearchAsync(string q)
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, $"/v1/geo/cities?q={q}", 200);
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private async Task<JsonElement> ProblemAsync(string url, int status)
    {
        (IScenarioResult result, JsonElement problem) = await GeoHttp.GetAsync(
            geo.Host,
            url,
            status
        );
        result.Context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
        return GeoHttp.FirstError(problem);
    }
}
