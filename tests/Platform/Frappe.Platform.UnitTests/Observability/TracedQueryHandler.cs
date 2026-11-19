using System.Diagnostics;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Answers with the trace and span ids it runs under.</summary>
public sealed class TracedQueryHandler : IQueryHandler<TracedQuery, TraceSnapshot>
{
    public const string SourceName = "Frappe.Tests.TraceFlow";

    public static readonly AsyncLocal<string?> Marker = new();

    private static readonly ActivitySource Source = new(SourceName);

    public async Task<Result<TraceSnapshot>> HandleAsync(TracedQuery query, CancellationToken cancellationToken)
    {
        Marker.Value = "set by the use case";
        await Task.Yield();
        await ResumeOnThreadPoolAsync();
        using var work = Source.StartActivity("work after the awaits");
        await ResumeOnThreadPoolAsync();
        return new TraceSnapshot(work!.TraceId.ToHexString(), work.ParentSpanId.ToHexString(), Marker.Value);
    }

    /// <summary>Completes on a thread-pool thread, so the caller resumes after a real thread hop and no timer.</summary>
    private static Task ResumeOnThreadPoolAsync()
    {
        var hop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.QueueUserWorkItem(static state => state.SetResult(), hop, preferLocal: false);
        return hop.Task;
    }
}
