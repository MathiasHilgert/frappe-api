using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

internal sealed class CountryCodeJsonConverter : StringValueJsonConverter<CountryCode>
{
    protected override ErrorOr<CountryCode> Create(string? value) => CountryCode.Create(value);

    protected override string Format(CountryCode value) => value.Value;
}
