namespace Frappe.Identity.Application.Credentials;

/// <summary>
/// The wait that makes two answers take as long: an answer is held until a floor of time has passed since its request began, so neither the
/// answer nor its time says which branch produced it (SEC-06). The floor must be above what the slowest branch takes.
/// </summary>
public static class ResponseFloor
{
    /// <summary>
    /// Waits until <paramref name="floor"/> has passed since <paramref name="started"/> (a <see cref="TimeProvider.GetTimestamp"/> of
    /// <paramref name="time"/>); returns at once when it has.
    /// </summary>
    public static async Task HoldAsync(TimeProvider time, long started, TimeSpan floor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);
        var remaining = floor - time.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, time, cancellationToken);
        }
    }
}
