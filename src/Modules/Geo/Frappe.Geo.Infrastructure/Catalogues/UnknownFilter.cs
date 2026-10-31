using Frappe.Kernel;

namespace Frappe.Geo.Infrastructure.Catalogues;

// A well-formed code the caller gave that is outside the catalogue is invalid input (422 at its field or parameter),
// not a missing resource.
internal static class UnknownFilter
{
    // Turns a not-found error into a validation error with the same code; any other error passes through.
    public static Error Field(Error error) =>
        error.Type == ErrorType.NotFound ? Error.Validation(error.Code) : error;

    public static Error Of(Error notFound, string pointer)
    {
        ErrorOr<Success> invalid = Field(notFound);
        return invalid.WithPointer(pointer).FirstError;
    }
}
