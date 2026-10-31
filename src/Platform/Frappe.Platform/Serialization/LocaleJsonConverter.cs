using ErrorOr;
using Frappe.Kernel.Localization;

namespace Frappe.Platform.Serialization;

internal sealed class LocaleJsonConverter : StringValueJsonConverter<Locale>
{
    protected override ErrorOr<Locale> Create(string? value) => Locale.Create(value);

    protected override string Format(Locale value) => value.Value;
}
