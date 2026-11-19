using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Throws a cancellation the caller did not ask for, as an inner timeout would.</summary>
public sealed class TimedOutQueryHandler : IQueryHandler<TimedOutQuery, string>
{
    public Task<Result<string>> HandleAsync(TimedOutQuery query, CancellationToken cancellationToken) =>
        throw new OperationCanceledException("Inner timeout.");
}
