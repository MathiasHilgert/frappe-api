namespace Frappe.TestUtilities;

/// <summary>
/// One test's share of a <see cref="FrappeNats"/> server: a stream, subject root and consumer prefix no other test
/// uses, so tests on one server run in parallel. Hosts that must exchange events share one scope.
/// </summary>
/// <param name="Url">The server URL.</param>
/// <param name="Root">The subject root and consumer prefix; the stream is its upper-case form.</param>
public sealed record NatsScope(string Url, string Root)
{
    /// <summary>Gets the stream name.</summary>
    public string Stream => Root.ToUpperInvariant();

    /// <summary>Gets the configuration a host needs to use this scope.</summary>
    public IEnumerable<KeyValuePair<string, string?>> Settings =>
        [
            new("Nats:Url", Url),
            new("Nats:SubjectRoot", Root),
            new("Nats:ConsumerPrefix", Root),
            new("Nats:Stream", Stream),
        ];

    /// <summary>Creates a scope with a fresh root on <paramref name="nats"/>.</summary>
    /// <param name="nats">The server.</param>
    /// <returns>The scope.</returns>
    public static NatsScope On(FrappeNats nats)
    {
        ArgumentNullException.ThrowIfNull(nats);
        return new(nats.Url, $"t{Guid.CreateVersion7():N}");
    }
}
