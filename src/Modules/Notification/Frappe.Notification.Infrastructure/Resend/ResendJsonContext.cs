using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>Resend's wire format: snake_case names, absent optional fields left out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResendEmail))]
[SuppressMessage("Maintainability", "CA1502:Avoid excessive complexity",
    Justification = "The complexity is in the source-generated serializer, not in hand-written code.")]
internal sealed partial class ResendJsonContext : JsonSerializerContext;
