using System.Reflection;
using Frappe.Identity.Application.Owners;
using Frappe.Identity.Domain;
using Frappe.Identity.Infrastructure;
using Frappe.Platform.Localization;
using Frappe.SharedKernel.Validation;
using Shouldly;

namespace Frappe.Identity.Tests;

/// <summary>Every identity error answers in the request locale: each code has a catalogue entry in every supported locale.</summary>
public sealed class IdentityErrorCatalogueTests
{
    private static readonly Assembly Catalogue = typeof(IdentityModule).Assembly;

    private static IEnumerable<Error> IdentityErrorValues() =>
        typeof(IdentityErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => (Error)f.GetValue(null)!)
            .Append(ResendVerificationCodeHandler.OwnerNotFound);

    [Fact]
    public void Catalogue_HasEveryKeyInEveryLocale_AndEveryMessageFormats() =>
        MessageCatalogConformance.Check(Catalogue).ShouldBeEmpty();

    [Fact]
    public void EveryIdentityErrorCode_HasACatalogueEntry() =>
        MessageCatalogConformance.CodesWithoutEntries(Catalogue, IdentityErrorValues().Select(e => e.Code)).ShouldBeEmpty();

    [Fact]
    public void EnglishEntries_MatchTheErrorMessages()
    {
        var english = new MessageCatalogSource(Catalogue).Load()[Frappe.SharedKernel.Localization.Locale.Parse("en")];

        foreach (var error in IdentityErrorValues())
            english.GetValueOrDefault(error.Code).ShouldBe(error.Message, error.Code);
    }
}
