using System.Reflection;
using System.Text.Json;
using Frappe.Results;

namespace Frappe.Notifications.Application.Messages;

/// <summary>
/// Reads the JSON payload of a request as the kind's own payload type, strictly: an unknown field, a field of the wrong type and a
/// required field that is missing or blank are refused as <c>notifications.payload.invalid</c> with the names of the fields, never their
/// values (S13). The same reading serves the command that accepts a request and the consumer that dispatches it.
/// </summary>
public static class NotificationPayloads
{
    /// <summary>The error code of a payload that is not the kind's.</summary>
    public const string InvalidCode = "notifications.payload.invalid";

    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };

    /// <summary>The payload as <see cref="NotificationKind.PayloadType"/>, or the error naming the fields that are wrong.</summary>
    public static Result<object> Read(NotificationKind kind, JsonElement payload, IEnumerable<INotificationPayloadCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return Invalid(kind, ["payload"]);
        }

        object? value;
        try
        {
            value = payload.Deserialize(kind.PayloadType, Strict);
        }
        catch (JsonException exception)
        {
            return Invalid(kind, [FieldOf(exception.Path)]);
        }

        if (value is null)
        {
            return Invalid(kind, ["payload"]);
        }

        return Validate(kind, value, checks);
    }

    /// <summary>
    /// The one validation both entry paths share: <paramref name="value"/> must be the kind's payload with every required field, within its own
    /// rules, name a recipient, and pass every channel's trial. It names fields, never values.
    /// </summary>
    public static Result<object> Validate(NotificationKind kind, object value, IEnumerable<INotificationPayloadCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(checks);
        if (!kind.PayloadType.IsInstanceOfType(value))
        {
            return Invalid(kind, ["payload"]);
        }

        var missing = MissingFields(value);
        if (missing.Length > 0)
        {
            return Invalid(kind, missing);
        }

        if (value is IPayloadRules rules && rules.InvalidFields() is { Count: > 0 } broken)
        {
            return Invalid(kind, [.. broken]);
        }

        try
        {
            _ = kind.RecipientOf(value);
        }
        catch (ArgumentException)
        {
            return Invalid(kind, ["recipient"]);
        }

        if (!checks.All(check => check.Accepts(kind, value)))
        {
            return Invalid(kind, ["payload"]);
        }

        return value;
    }

    private static InvalidValueError Invalid(NotificationKind kind, string[] fields) =>
        Error.InvalidValue(InvalidCode, ("kind", kind.Name), ("fields", fields));

    /// <summary>The JSON name of a failing path (<c>$.email</c> is <c>email</c>); the whole payload when the path names none.</summary>
    private static string FieldOf(string? path) =>
        path is { Length: > 2 } && path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : "payload";

    /// <summary>The properties that are not nullable and hold nothing (null, or only white space for text), by their JSON names.</summary>
    private static string[] MissingFields(object value)
    {
        var context = new NullabilityInfoContext();
        return
        [
            .. value.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0
                    && !property.PropertyType.IsValueType
                    && context.Create(property).ReadState == NullabilityState.NotNull
                    && IsEmpty(property.GetValue(value)))
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)),
        ];
    }

    private static bool IsEmpty(object? value) => value is null || (value is string text && string.IsNullOrWhiteSpace(text));
}
