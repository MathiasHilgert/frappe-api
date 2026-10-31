using Microsoft.Extensions.Logging;

namespace Frappe.Geo.Infrastructure;

internal static partial class Log
{
    [LoggerMessage(
        1000,
        LogLevel.Warning,
        "Refreshing the {Catalogue} snapshot failed; serving the previous snapshot"
    )]
    public static partial void RefreshFailed(ILogger logger, string catalogue, Exception exception);
}
