using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

internal sealed class SubdivisionCodeJsonConverter : StringValueJsonConverter<SubdivisionCode>
{
    protected override ErrorOr<SubdivisionCode> Create(string? value) =>
        SubdivisionCode.Create(value);

    protected override string Format(SubdivisionCode value) => value.Value;
}
