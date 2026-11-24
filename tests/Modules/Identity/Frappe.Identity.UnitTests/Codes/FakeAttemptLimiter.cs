using Frappe.Identity.Application.Limits;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>A limiter that answers what the test says and remembers every attempt it was asked to count.</summary>
internal sealed class FakeAttemptLimiter : IAttemptLimiter
{
    public AttemptVerdict Answer { get; set; } = AttemptVerdict.Allowed;

    public List<(LimitFlow Flow, string Subject)> Hits { get; } = [];

    public Task<AttemptVerdict> HitAsync(LimitFlow flow, string subjectKey, CancellationToken cancellationToken)
    {
        Hits.Add((flow, subjectKey));
        return Task.FromResult(Answer);
    }
}
