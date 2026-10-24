namespace Frappe.Identity.Application.Security;

/// <summary>
/// A hash of a random secret, made once per process with the current hashing cost. Checking a password against it
/// when an email is unknown costs what a real check costs, so response time does not reveal which emails exist.
/// </summary>
public sealed class DecoyPassword(IPasswordHasher hasher, ISecretGenerator secrets)
{
    private readonly Lazy<Task<string>> _hash = new(
        () => hasher.HashAsync(secrets.NewSessionToken(), CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Spends one verification; the answer is always false.</summary>
    public async Task VerifyAsync(string password, CancellationToken cancellationToken) =>
        await hasher.VerifyAsync(password, await _hash.Value.WaitAsync(cancellationToken), cancellationToken);
}
