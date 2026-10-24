using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Frappe.Platform.Messaging.Sealing;

/// <summary>The message seal key. A secret: set it through the environment, never in a committed file.</summary>
public sealed class MessageSealOptions
{
    public const string SectionName = "Messaging:Seal";

    /// <summary>The key committed in appsettings.Development.json; refused anywhere but Development.</summary>
    public const string DevelopmentKey = "development-only-message-seal-key-do-not-deploy";

    /// <summary>
    /// Whether <paramref name="key"/> is the development key in disguise: compared as bytes after decoding, so
    /// surrounding whitespace, a change of case or a Base64 encoding of it is still the public key.
    /// </summary>
    public static bool IsDevelopmentKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        var development = Normalized(Encoding.UTF8.GetBytes(DevelopmentKey));
        var trimmed = key.Trim();
        if (Normalized(Encoding.UTF8.GetBytes(trimmed)).AsSpan().SequenceEqual(development))
            return true;

        var buffer = new byte[trimmed.Length];
        return Convert.TryFromBase64String(trimmed, buffer, out var written)
            && Normalized(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(buffer, 0, written).Trim())).AsSpan().SequenceEqual(development);
    }

    // ASCII case folding only: the development key is ASCII, and any other byte simply never matches.
    private static byte[] Normalized(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is >= (byte)'A' and <= (byte)'Z')
                bytes[i] = (byte)(bytes[i] + ('a' - 'A'));
        }

        return bytes;
    }

    [Required]
    [MinLength(32)]
    public string? Key { get; set; }

    /// <summary>
    /// The key being rotated out, set while messages it sealed may still sit in the outbox or a queue: seals are opened
    /// with it but never made. Drop it once those messages are gone (a code expires in minutes; a day is generous).
    /// </summary>
    [MinLength(32)]
    public string? PreviousKey { get; set; }
}
