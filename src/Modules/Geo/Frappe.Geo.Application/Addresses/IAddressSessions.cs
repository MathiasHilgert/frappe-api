namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// Outbound port: which search sessions are open, shared by every replica, so a session's first suggestion and its
/// closing resolution can be counted once each (S15). A marker holds nothing but the session's existence and expires
/// with the provider's session lifetime.
/// </summary>
public interface IAddressSessions
{
    /// <summary>Opens <paramref name="session"/> for <paramref name="lifetime"/>; <see langword="true"/> only when it was not open yet.</summary>
    Task<bool> OpenAsync(string provider, AddressSession session, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Closes <paramref name="session"/>; <see langword="true"/> only when it was open (suggested within its lifetime and not closed yet).</summary>
    Task<bool> CloseAsync(string provider, AddressSession session, CancellationToken cancellationToken);
}
