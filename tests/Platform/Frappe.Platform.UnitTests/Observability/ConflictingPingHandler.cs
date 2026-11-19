using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Generic, so assembly scanning skips it; tests register it by hand to simulate a second handler.</summary>
public sealed class ConflictingPingHandler<TMarker> : IQueryHandler<Ping, string>
{
    public Task<Result<string>> HandleAsync(Ping query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(typeof(TMarker).Name);
}
