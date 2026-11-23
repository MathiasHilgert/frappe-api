using DotNet.Testcontainers.Images;

namespace Frappe.EndToEnd.Tests;

/// <summary>
/// The image a run is handed instead of building one: CI builds <c>deploy/Dockerfile</c> once (with its layer cache) and names it in
/// <see cref="Variable"/>; a local run leaves it unset and the session builds the image itself.
/// </summary>
public static class PrebuiltImage
{
    public const string Variable = "FRAPPE_E2E_IMAGE";

    /// <summary>The named image, or <see langword="null"/> when the variable is unset or blank.</summary>
    public static IImage? From(Func<string, string?> environment)
    {
        var name = environment(Variable);
        return string.IsNullOrWhiteSpace(name) ? null : new DockerImage(name.Trim());
    }
}
