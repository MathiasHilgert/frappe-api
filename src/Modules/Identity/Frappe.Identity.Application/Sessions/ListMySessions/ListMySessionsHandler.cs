using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>Handles <see cref="ListMySessions"/>: reads one session more than the page holds to know whether another page follows.</summary>
internal sealed class ListMySessionsHandler(ISessionReads reads, TimeProvider time) : IQueryHandler<ListMySessions, MySessionsPage>
{
    public async Task<Result<MySessionsPage>> HandleAsync(ListMySessions query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var read = await reads.ListLiveAsync(query.PrincipalId, now, query.Limit + 1, query.After, cancellationToken);
        var page = read.Take(query.Limit).ToList();
        var next = read.Count > query.Limit ? new SessionPosition(page[^1].OpenedAt, page[^1].Id) : null;
        return new MySessionsPage([.. page.Select(session => ViewOf(session, query.CurrentSessionId))], next);
    }

    private static MySessionView ViewOf(ListedSession session, Guid current)
    {
        var agent = UserAgentParser.Parse(session.UserAgent);
        return new MySessionView(session.Id, session.Id == current, agent.Browser, agent.Os, session.IpPrefix, session.OpenedAt, session.LastSeenAt);
    }
}
