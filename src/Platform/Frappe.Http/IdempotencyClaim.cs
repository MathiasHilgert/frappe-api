namespace Frappe.Http;

/// <summary>
/// The outcome of a claim; for <see cref="IdempotencyOutcome.Replay"/>, the answer to replay; for
/// <see cref="IdempotencyOutcome.Claimed"/>, the <see cref="Owner"/> token that completes or releases this claim and no
/// other one on the same key.
/// </summary>
public sealed record IdempotencyClaim(IdempotencyOutcome Outcome, StoredResponse? Response = null, string? Owner = null);
