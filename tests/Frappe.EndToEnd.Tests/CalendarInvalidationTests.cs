using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Frappe.EndToEnd.Tests;

/// <summary>
/// The real way a calendar change reaches a cached answer: the Api writes the change and its <c>HolidaysChanged</c>
/// in Geo's outbox, the Worker relays it to NATS JetStream, consumes it (<c>geo-calendar-cache</c>) and evicts the
/// calendar after the commit, and the Valkey backplane carries the eviction to the Api, which answers anew well before
/// its cache would expire.
/// </summary>
[ClassDataSource<DeployedFrappe>(Shared = SharedType.PerTestSession)]
public sealed class CalendarInvalidationTests(DeployedFrappe stack)
{
    // Far below the ten minutes a cached calendar lives, so only the eviction can explain a new answer.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // A Tuesday of a year no other test uses.
    private const string Day = "2031-03-11";

    [Test]
    public async Task A_correction_made_through_the_Api_reaches_its_cached_answer_through_the_Worker()
    {
        // Every source points to a reserved name, so the calendar is seeded straight into the database: an empty general
        // calendar of 2031 for Argentina, which is what an import of a year without holidays leaves.
        var seeded = await stack.Postgres.ExecAsync(
            ["psql", "-U", "frappe_owner", "-d", "frappe", "-v", "ON_ERROR_STOP=1", "-c",
             "INSERT INTO geo.holiday_calendars (jurisdiction, year, scope, revision) VALUES ('AR', 2031, 'general', 0)"]);
        await Assert.That(seeded.ExitCode).IsEqualTo(0L).Because(seeded.Stderr);

        var before = await BusinessDayAsync();
        using var put = await stack.Api.PutAsJsonAsync(
            "/v1/geo/countries/AR/holidays/overrides", new { date = Day, name = "Feriado de prueba", kind = "holiday" });
        var flipped = await EventuallyAsync(async () => await BusinessDayAsync() == false);

        await Assert.That(before).IsTrue();
        await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(flipped).IsTrue().Because("the eviction never reached the Api" + await stack.LogsAsync());
    }

    private async Task<bool?> BusinessDayAsync()
    {
        using var response = await stack.Api.GetAsync($"/v1/geo/countries/AR/calendar/{Day}?policy=commercial");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessDay").GetBoolean();
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        return false;
    }
}
