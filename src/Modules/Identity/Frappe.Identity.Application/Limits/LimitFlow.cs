namespace Frappe.Identity.Application.Limits;

/// <summary>
/// One kind of attempt a feature counts, declared by the feature in code under a stable <paramref name="Name"/>: how many attempts one subject
/// may make in a <paramref name="Window"/>, and what happens when the counter is down (<paramref name="OnFailure"/>). The numbers are
/// production defaults an operator may only tighten (<c>Identity:Limits:&lt;name&gt;</c>); what an outage does is a security decision of the
/// flow and is never configured.
/// </summary>
/// <param name="Name">
/// Lower case, dot separated, unique across the module (<c>code.guess</c>); it is also the configuration key and a metric tag.
/// </param>
/// <param name="Limit">The attempts one subject may make per window; at least 1.</param>
/// <param name="Window">How long a window lasts from its first attempt.</param>
/// <param name="OnFailure">What an unreachable counter does.</param>
public sealed record LimitFlow(string Name, int Limit, TimeSpan Window, LimitFailure OnFailure)
{
    /// <summary>The flow's name, which is also its configuration key.</summary>
    public string Name { get; } = !string.IsNullOrWhiteSpace(Name) ? Name : throw new ArgumentException("A flow needs a name.", nameof(Name));

    /// <summary>The attempts one subject may make per window.</summary>
    public int Limit { get; } = Limit >= 1 ? Limit : throw new ArgumentOutOfRangeException(nameof(Limit), "A flow allows at least one attempt.");

    /// <summary>How long a window lasts from its first attempt.</summary>
    public TimeSpan Window { get; } =
        Window > TimeSpan.Zero ? Window : throw new ArgumentOutOfRangeException(nameof(Window), "A window has a length.");
}
