using ErrorOr;
using Frappe.Kernel.Geography;

namespace Frappe.Platform.Serialization;

internal sealed class PostalCodeJsonConverter : StringValueJsonConverter<PostalCode>
{
    protected override ErrorOr<PostalCode> Create(string? value) => PostalCode.Create(value);

    protected override string Format(PostalCode value) => value.Value;
}
