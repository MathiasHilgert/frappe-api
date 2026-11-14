namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// The address provider is not configured or did not answer (a timeout, a rejected key, an exhausted quota, an outage).
/// Its message names the reason only: never what was searched or resolved.
/// </summary>
public class AddressProviderUnavailableException : Exception
{
    public AddressProviderUnavailableException()
        : this("The address provider is not available.")
    {
    }

    public AddressProviderUnavailableException(string message)
        : base(message)
    {
    }

    public AddressProviderUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
