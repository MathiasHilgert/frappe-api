using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

internal sealed class TimeZoneIdJsonConverter : StringValueJsonConverter<TimeZoneId>
{
    protected override ErrorOr<TimeZoneId> Create(string? value) => TimeZoneId.Create(value);

    protected override string Format(TimeZoneId value) => value.Value;
}
