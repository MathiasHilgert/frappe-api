namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>How a run of a source ended. Stored by name in <c>geo.sync_runs</c>.</summary>
internal enum SyncOutcome
{
    /// <summary>The snapshot was applied (it may have changed nothing the data already held).</summary>
    Applied,

    /// <summary>Every file was unchanged (304, or the same SHA-256 as the last success): nothing was read.</summary>
    Unchanged,

    /// <summary>A file is not published (404): the run changed nothing and the next one asks again.</summary>
    NotPublished,

    /// <summary>The download was read and refused as a whole by its validations: nothing was applied.</summary>
    Refused,

    /// <summary>The source could not be reached or answered something else: nothing was applied.</summary>
    Failed,
}
