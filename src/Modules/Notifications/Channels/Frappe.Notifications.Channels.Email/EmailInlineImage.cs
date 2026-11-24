namespace Frappe.Notifications.Channels.Email;

/// <summary>
/// An image the HTML shows through a <c>cid:</c> reference and the provider sends as an inline attachment: the official Frappé mark, in the
/// two inks the design allows (a black disc for light grounds, a white disc for dark ones). The bytes are the brand files, never redrawn.
/// </summary>
/// <param name="FileName">The attachment's file name.</param>
/// <param name="ContentId">The id the <c>cid:</c> reference in the HTML names.</param>
/// <param name="ContentType">The media type of <see cref="Bytes"/>.</param>
/// <param name="Bytes">The image.</param>
public sealed record EmailInlineImage(string FileName, string ContentId, string ContentType, byte[] Bytes)
{
    /// <summary>The content id of the logo shown on a light ground.</summary>
    public const string LightLogoId = "frappe-logo-light";

    /// <summary>The content id of the logo shown on a dark ground.</summary>
    public const string DarkLogoId = "frappe-logo-dark";

    private const string ResourcePrefix = "frappe.email_assets/";

    /// <summary>Both logos, always in the same order, so the provider gets the same bytes on every attempt.</summary>
    public static IReadOnlyList<EmailInlineImage> Logos { get; } =
        [Load("frappe-logo-light.png", LightLogoId), Load("frappe-logo-dark.png", DarkLogoId)];

    public override string ToString() => $"EmailInlineImage {{ ContentId = {ContentId}, Length = {Bytes.Length} }}";

    private static EmailInlineImage Load(string fileName, string contentId)
    {
        using var stream = typeof(EmailInlineImage).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException($"The embedded email asset '{fileName}' is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new EmailInlineImage(fileName, contentId, "image/png", memory.ToArray());
    }
}
