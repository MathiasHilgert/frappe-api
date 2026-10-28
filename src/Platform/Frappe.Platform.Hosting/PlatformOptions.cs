using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Hosting;

/// <summary>The process-wide platform settings, bound from the <c>Platform</c> section and validated on start.</summary>
public sealed class PlatformOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "Platform";

    /// <summary>Gets the role this process runs; <see cref="HostRole.All"/> when not configured.</summary>
    [EnumDataType(typeof(HostRole))]
    public HostRole Role { get; init; } = HostRole.All;
}
