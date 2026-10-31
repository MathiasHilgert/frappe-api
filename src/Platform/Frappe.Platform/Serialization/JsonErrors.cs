using System.Text.Json;
using ErrorOr;

namespace Frappe.Platform.Serialization;

internal static class JsonErrors
{
    // The codes stay in the message so a malformed-request problem can name the rule that failed.
    public static JsonException From(IReadOnlyList<Error> errors) =>
        new(string.Join("; ", errors.Select(static e => $"{e.Code}: {e.Description}")));
}
