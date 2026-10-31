using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Http;

internal static partial class Log
{
    [LoggerMessage(201, LogLevel.Error, "Request {Path} failed with an unhandled exception")]
    public static partial void Unhandled(ILogger logger, Exception exception, string path);

    [LoggerMessage(202, LogLevel.Error, "Request {Path} returned a server error with {ErrorCodes}")]
    public static partial void ServerErrorReturned(ILogger logger, string path, string errorCodes);
}
