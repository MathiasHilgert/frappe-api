using Alba;
using Frappe.Platform.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Tracking;

namespace Frappe.TestUtilities;

/// <summary>Runs work on a test host and waits for every message it started, cascades included.</summary>
public static class TrackedMessages
{
    /// <summary>How long a tracked session waits for its messages before it fails.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="act"/> in a fresh service scope, inside <paramref name="tenant"/>'s scope when one
    /// is given, and waits for every message it started.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="tenant">The business the work runs for, or <see langword="null"/> for none.</param>
    /// <param name="act">The work, given the scope's services.</param>
    /// <param name="assertExceptions">Whether a handler exception fails the session.</param>
    /// <param name="alsoTrack">Other hosts whose messages the session waits for, such as a worker.</param>
    /// <returns>The finished session.</returns>
    public static Task<ITrackedSession> TrackAsync(
        this IAlbaHost host,
        Guid? tenant,
        Func<IServiceProvider, Task> act,
        bool assertExceptions = true,
        params IHost[] alsoTrack
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        // Integration events leave through NATS, so the session follows them there and into every host.
        TrackedSessionConfiguration tracking = host.TrackActivity()
            .Timeout(Timeout)
            .IncludeExternalTransports()
            .AlsoTrack(alsoTrack);
        if (!assertExceptions)
        {
            tracking = tracking.DoNotAssertOnExceptionsDetected();
        }

        return tracking.ExecuteAndWaitAsync(
            (Func<IMessageContext, Task>)(
                async _ =>
                {
                    using TenantScope? scope = tenant is { } businessId
                        ? TenantScope.Begin(
                            businessId,
                            branchId: null,
                            TenantScope.SystemPrincipalId
                        )
                        : null;
                    await using AsyncServiceScope services = host.Services.CreateAsyncScope();
                    await act(services.ServiceProvider);
                }
            )
        );
    }
}
