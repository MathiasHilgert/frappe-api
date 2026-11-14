namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// This host has no address provider (none composed, or one without its credentials): a deployment's setting, not an
/// outage, so it answers <see cref="GeoErrors.AddressesNotConfigured"/> rather than "try again later".
/// </summary>
public sealed class AddressProviderNotConfiguredException : AddressProviderUnavailableException
{
    public AddressProviderNotConfiguredException()
        : this("No address provider is configured.")
    {
    }

    public AddressProviderNotConfiguredException(string message)
        : base(message)
    {
    }

    public AddressProviderNotConfiguredException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
