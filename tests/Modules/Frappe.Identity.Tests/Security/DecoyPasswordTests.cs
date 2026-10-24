using Frappe.Identity.Application.Security;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class DecoyPasswordTests
{
    [Fact]
    public async Task Verify_HashesTheDecoyOnce_AndPaysOneVerificationPerCall()
    {
        var hasher = new CountingHasher();
        var decoy = new DecoyPassword(hasher, new FixedSecrets());

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => decoy.VerifyAsync("guess", TestContext.Current.CancellationToken)));

        hasher.Hashes.ShouldBe(1);
        hasher.Verifications.ShouldBe(5);
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        private int _hashes;
        private int _verifications;

        public int Hashes => _hashes;
        public int Verifications => _verifications;

        public Task<string> HashAsync(string password, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _hashes);
            return Task.FromResult($"hash:{password}");
        }

        public Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _verifications);
            return Task.FromResult(false);
        }

        public bool NeedsRehash(string passwordHash) => false;
    }

    private sealed class FixedSecrets : ISecretGenerator
    {
        public string NewSessionToken() => "decoy-secret";

        public string NewVerificationCode() => "000000";
    }
}
