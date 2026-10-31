using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Http;

internal static partial class Log
{
    [LoggerMessage(201, LogLevel.Error, "Request {Path} failed with an unhandled exception")]
    public static partial void Unhandled(ILogger logger, Exception exception, string path);
}
