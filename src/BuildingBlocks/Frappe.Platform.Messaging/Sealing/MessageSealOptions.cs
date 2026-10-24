using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Messaging.Sealing;

/// <summary>The message seal key. A secret: set it through the environment, never in a committed file.</summary>
public sealed class MessageSealOptions
{
    public const string SectionName = "Messaging:Seal";

    /// <summary>The key committed in appsettings.Development.json; refused anywhere but Development.</summary>
    public const string DevelopmentKey = "development-only-message-seal-key-do-not-deploy";

    [Required]
    [MinLength(32)]
    public string? Key { get; set; }
}
