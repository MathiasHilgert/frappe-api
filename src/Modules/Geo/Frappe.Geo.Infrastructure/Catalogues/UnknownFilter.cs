using Frappe.Kernel;

namespace Frappe.Geo.Infrastructure.Catalogues;

// A well-formed filter code outside the catalogue is invalid input (422 at the parameter), not a missing resource.
internal static class UnknownFilter
{
    public static Error Of(Error notFound, string pointer)
    {
        ErrorOr<Success> invalid = Error.Validation(notFound.Code);
        return invalid.WithPointer(pointer).FirstError;
    }
}
