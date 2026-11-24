using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

// EF Core materializes the entity through its private constructor and setters; behavior arrives with the feedback handling.
#pragma warning disable S1144, S3453
namespace Frappe.Notifications.Domain.Suppressions;

/// <summary>An address that must not be contacted over a channel again. Suppressions never expire (S11) and hold only the keyed hash (S13).</summary>
public sealed class Suppression
{
    private Suppression()
    {
        Channel = null!;
        AddressHash = null!;
    }

    public string Channel { get; private set; }

    /// <summary>The keyed hash of the normalized address.</summary>
    public byte[] AddressHash { get; private set; }

    /// <summary>The id of the key that made <see cref="AddressHash"/>; the gate probes every active key.</summary>
    public short HashKeyId { get; private set; }

    public SuppressionReason Reason { get; private set; }

    public SuppressionSource Source { get; private set; }

    /// <summary>The provider that reported it, when one did; a reference nothing enforces.</summary>
    public DeliveryProvider? Provider { get; private set; }

    /// <summary>The provider's id of the event that reported it, when one did; a reference nothing enforces.</summary>
    public string? ProviderEventId { get; private set; }

    public Instant CreatedAt { get; private set; }
}
