using ErrorOr;
using Frappe.Kernel.Currencies;

namespace Frappe.Platform.Serialization;

internal sealed class CurrencyCodeJsonConverter : StringValueJsonConverter<CurrencyCode>
{
    protected override ErrorOr<CurrencyCode> Create(string? value) => CurrencyCode.Create(value);

    protected override string Format(CurrencyCode value) => value.Value;
}
