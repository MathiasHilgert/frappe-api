using Frappe.Geo.Application.Cities;

namespace Frappe.Geo.UnitTests.Cities;

public sealed class CitySearchTermsTests
{
    [Fact]
    public void A_query_folds_and_splits_on_spaces_and_commas()
    {
        CitySearchTerms terms = CitySearchTerms.Create("Pilar, Buenos  Aires").Value;

        terms.Plain.ShouldBe("pilar, buenos aires");
        terms.Tokens.Select(static token => token.Plain).ShouldBe(["pilar", "buenos", "aires"]);
    }

    [Fact]
    public void The_plain_form_keeps_wildcards_and_the_escaped_form_escapes_the_backslash_first()
    {
        CitySearchTerms terms = CitySearchTerms.Create(@"a\_b 50%").Value;

        terms.Plain.ShouldBe(@"a\_b 50%");
        terms.Escaped.ShouldBe(@"a\\\_b 50\%");
        terms.Tokens.Select(static token => token.Escaped).ShouldBe([@"a\\\_b", @"50\%"]);
    }

    [Fact]
    public void Only_the_first_five_words_count()
    {
        CitySearchTerms terms = CitySearchTerms.Create("a b c d e f g").Value;

        terms.Tokens.Select(static token => token.Plain).ShouldBe(["a", "b", "c", "d", "e"]);
    }

    [Theory]
    [InlineData("pi", false)]
    [InlineData("v m", true)]
    [InlineData("pil", true)]
    public void Similar_spellings_count_from_three_folded_characters(string q, bool fuzzy)
    {
        CitySearchTerms.Create(q).Value.Fuzzy.ShouldBe(fuzzy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("p")]
    [InlineData("   ")]
    [InlineData(" p ")]
    [InlineData(",,")]
    public void A_query_under_two_folded_characters_or_without_a_word_is_invalid(string? q)
    {
        CitySearchTerms.Create(q).FirstError.Code.ShouldBe("geo.city.query_invalid");
    }

    [Fact]
    public void A_query_is_at_most_one_hundred_folded_characters()
    {
        CitySearchTerms.Create(new string('a', 100)).IsError.ShouldBeFalse();
        CitySearchTerms
            .Create(new string('a', 101))
            .FirstError.Code.ShouldBe("geo.city.query_invalid");
    }
}
