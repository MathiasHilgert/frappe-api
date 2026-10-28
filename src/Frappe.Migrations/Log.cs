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

    // Npgsql and EF exception messages name keys, hosts and users, never the password.
    [LoggerMessage(
        3,
        LogLevel.Critical,
        "Migrations or seed failed; nothing after the failing migration or module was applied"
    )]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(4, LogLevel.Information, "Seeded {Count} reference row(s)")]
    public static partial void Seeded(ILogger logger, int count);
}
