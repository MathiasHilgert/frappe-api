using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain;

public static class IdentityErrors
{
    public static readonly Error NameRequired = new("identity.name.required", "Name is required.", "name");

    public static readonly Error NameTooLong =
        new("identity.name.too_long", $"Name must be at most {PersonName.MaxLength} characters.", "name");
}
