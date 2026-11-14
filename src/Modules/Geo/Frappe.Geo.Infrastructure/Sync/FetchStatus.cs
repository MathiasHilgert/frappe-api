namespace Frappe.Geo.Infrastructure.Sync;

internal enum FetchStatus
{
    /// <summary>304: unchanged since the validators stored for it.</summary>
    NotModified,

    /// <summary>404: the source has not published it (yet).</summary>
    NotPublished,

    /// <summary>200 with a body.</summary>
    Content,
}
