using Frappe.Identity.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Every identity setting below its floor stops the host before it serves a request.</summary>
public sealed class IdentityOptionsTests(ApiFixture fixture)
{
    public static TheoryData<string, Action<IServiceCollection>> InvalidOptions => new()
    {
        { "Argon2Options.MemorySizeKib", s => s.Configure<Argon2Options>(o => o.MemorySizeKib = 1024) },
        { "Argon2Options.Iterations", s => s.Configure<Argon2Options>(o => o.Iterations = 1) },
        { "Argon2Options.Parallelism", s => s.Configure<Argon2Options>(o => o.Parallelism = 17) },
        { "'Identity:SecretHashing:Key'", s => s.Configure<SecretHashingOptions>(o => o.Key = null) },
        { "'Identity:SecretHashing:Key'", s => s.Configure<SecretHashingOptions>(o => o.Key = new string(' ', 40)) },
        { "'Identity:SecretHashing:Key'", s => s.Configure<SecretHashingOptions>(o => o.Key = "too-short") },
        { "AttemptLimiterOptions.TimeoutMilliseconds", s => s.Configure<AttemptLimiterOptions>(o => o.TimeoutMilliseconds = 5) },
        { "BreachedPasswordOptions.TimeoutMilliseconds", s => s.Configure<BreachedPasswordOptions>(o => o.TimeoutMilliseconds = 20_000) },
        { "'Identity:BreachedPasswords:BaseAddress'", s => s.Configure<BreachedPasswordOptions>(o => o.BaseAddress = new Uri("/relative", UriKind.Relative)) },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task InvalidOptions_StopTheHostFromStarting(string setting, Action<IServiceCollection> configure)
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => fixture.CreateHostAsync(configure));

        exception.Message.ShouldContain(setting);
    }
}
