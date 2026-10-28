using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Messaging;

internal static partial class Log
{
    [LoggerMessage(
        101,
        LogLevel.Warning,
        "Message {MessageType} was rejected with {ErrorCodes} and acknowledged without retry"
    )]
    public static partial void Rejected(ILogger logger, string messageType, string errorCodes);
}
