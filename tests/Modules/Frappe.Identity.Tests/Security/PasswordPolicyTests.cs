using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Shouldly;

namespace Frappe.Identity.Tests.Security;

public sealed class PasswordPolicyTests
{
    [Fact]
    public async Task ValidateAsync_BreachedPassword_ReportsBreached()
    {
        var policy = new PasswordPolicy(new FixedChecker(breached: true));

        var result = await policy.ValidateAsync("password1234", TestContext.Current.CancellationToken);

        result.Errors.ShouldBe([IdentityErrors.PasswordBreached]);
    }

    [Fact]
    public async Task ValidateAsync_UnbreachedPassword_Succeeds()
    {
        var policy = new PasswordPolicy(new FixedChecker(breached: false));

        var result = await policy.ValidateAsync("a long and unseen phrase", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TooShort_SkipsTheBreachCheck()
    {
        var checker = new FixedChecker(breached: true);

        var result = await new PasswordPolicy(checker).ValidateAsync("short", TestContext.Current.CancellationToken);

        result.Errors.ShouldBe([IdentityErrors.PasswordTooShort]);
        checker.Calls.ShouldBe(0);
    }

    private sealed class FixedChecker(bool breached) : IBreachedPasswordChecker
    {
        public int Calls { get; private set; }

        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(breached);
        }
    }
}
