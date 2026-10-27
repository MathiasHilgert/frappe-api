using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform;

internal sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    [EnumDataType(typeof(HostRole))]
    public HostRole Role { get; init; } = HostRole.All;
}
