using Microsoft.Extensions.Logging;

namespace Frappe.Migrations;

internal static partial class Log
{
    [LoggerMessage(
        1,
        LogLevel.Error,
        "Usage: Frappe.Migrations --connection <owner connection string>"
    )]
    public static partial void Usage(ILogger logger);

    [LoggerMessage(2, LogLevel.Information, "Applied {Count} migration(s): {Migrations}")]
    public static partial void Applied(ILogger logger, int count, string migrations);
}
