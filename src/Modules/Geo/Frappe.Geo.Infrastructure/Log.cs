using Microsoft.Extensions.Logging;

namespace Frappe.Geo.Infrastructure;

internal static partial class Log
{
    [LoggerMessage(2000, LogLevel.Information, "Loaded the {Catalogue} catalogue with {Rows} rows")]
    public static partial void CatalogueLoaded(ILogger logger, string catalogue, int rows);
}
