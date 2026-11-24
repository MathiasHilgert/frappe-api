using Frappe.Aggregates;
using NodaTime;

namespace Frappe.Identity.Domain.Owners;

/// <summary>Somebody tried to register the address of a verified owner (S3); the real owner is told. The address is personal data (DOM-06).</summary>
/// <param name="Email">The address as the real owner holds it.</param>
/// <param name="Language">The real owner's language.</param>
/// <param name="At">When the registration was attempted.</param>
public sealed record OwnerRegistrationAttempted(string Email, string Language, Instant At) : IDomainEvent
{
    public override string ToString() => $"OwnerRegistrationAttempted {{ Email = ***, Language = {Language}, At = {At} }}";
}
