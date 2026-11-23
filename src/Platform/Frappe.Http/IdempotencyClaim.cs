namespace Frappe.Http;

/// <summary>The outcome of a claim and, for <see cref="IdempotencyOutcome.Replay"/>, the answer to replay.</summary>
public sealed record IdempotencyClaim(IdempotencyOutcome Outcome, StoredResponse? Response = null);
