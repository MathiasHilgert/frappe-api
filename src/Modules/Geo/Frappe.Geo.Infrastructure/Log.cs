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

    [LoggerMessage(
        1001,
        LogLevel.Error,
        "Building the {Catalogue} snapshot failed; answering 503 until it succeeds"
    )]
    public static partial void BuildFailed(ILogger logger, string catalogue, Exception exception);
}
