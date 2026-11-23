using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Answers the sample query.</summary>
public sealed class PingHandler : IQueryHandler<Ping, string>
{
    public Task<Result<string>> HandleAsync(Ping query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>($"pong:{query.Marker}");
}
