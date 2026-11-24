namespace Frappe.Identity.Domain.Credentials;

/// <summary>The encoded hash of a password, as its hasher wrote it with its own algorithm, parameters and salt. Opaque to everything else.</summary>
public sealed record PasswordHash
{
    /// <summary>The hash as stored.</summary>
    public PasswordHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>The encoded text.</summary>
    public string Value { get; }

    public override string ToString() => "***";
}
