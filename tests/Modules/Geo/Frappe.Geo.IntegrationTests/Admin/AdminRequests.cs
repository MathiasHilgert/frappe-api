using System.Net;
using Frappe.Geo.IntegrationTests.Hosts;
using Frappe.Geo.IntegrationTests.Persistence;

namespace Frappe.Geo.IntegrationTests.Admin;

/// <summary>Shorthands for the administration tests: requests through the real Api and the stable code of an error.</summary>
internal static class AdminRequests
{
    public static Task<GeoApi.Answer> SendAsync(GeoDatabase database, ValkeyCache valkey, HttpMethod method, string path, object? body = null) =>
        GeoApi.SendAsync(database, valkey, method, path, body);

    public static string? ErrorCode(GeoApi.Answer answer) =>
        answer.Body.ValueKind == System.Text.Json.JsonValueKind.Object && answer.Body.TryGetProperty("errors", out var errors)
            ? errors[0].GetProperty("code").GetString()
            : null;

    public static (HttpStatusCode Status, string? Code) Outcome(GeoApi.Answer answer) => (answer.Status, ErrorCode(answer));

    /// <summary>Waits until a request blocks on a row another writer holds, so the race is deterministic.</summary>
    public static async Task WaitForALockWaitAsync(GeoDatabase database, Task request)
    {
        for (var attempt = 0; attempt < 200 && !request.IsCompleted; attempt++)
        {
            var waiting = await database.QueryAsync("SELECT count(*) FROM pg_locks WHERE NOT granted AND pid IN (SELECT pid FROM pg_stat_activity WHERE datname = current_database())", reader => reader.GetInt64(0));
            if (waiting[0] > 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("The request never waited for the other writer.");
    }
}
