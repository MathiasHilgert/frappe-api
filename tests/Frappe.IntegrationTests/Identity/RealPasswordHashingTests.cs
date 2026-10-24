using System.Net;
using System.Text.Json;
using Alba;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>
/// The shared host swaps Argon2id for <see cref="FastPasswordHasher"/>; these tests boot one host with the real
/// hasher and walk register, log in and change password end to end so production hashing stays covered.
/// </summary>
public sealed class RealPasswordHashingTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string NewPassword = "a brand new passphrase";
    private IAlbaHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await fixture.CreateHostAsync(realPasswordHasher: true);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Register_StoresAnArgon2idHash()
    {
        var result = await _host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.Post.Json(new { name = "Ana", email = OwnerSeeder.NewEmail(), password = OwnerSeeder.Password }).ToUrl("/v1/identity/owners");
            s.StatusCodeShouldBe(201);
        });

        var id = (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await SessionSeeder.LoadAsync(_host, id)).Owner.PasswordHash.ShouldStartWith("$argon2id$v=19$");
    }

    [Fact]
    public async Task LogIn_ThenChangePassword_VerifiesAndRehashesWithArgon2id()
    {
        var email = OwnerSeeder.NewEmail();
        var owner = await OwnerSeeder.SeedAsync(_host, email);
        await LogInAsync(email, "wrong password!!", 401);
        var token = (await (await LogInAsync(email, OwnerSeeder.Password, 201)).ReadAsJsonAsync<JsonElement>())
            .GetProperty("token").GetString();

        await _host.Scenario(s =>
        {
            s.Put.Json(new { currentPassword = OwnerSeeder.Password, newPassword = NewPassword }).ToUrl("/v1/identity/me/password");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(204);
        });

        (await SessionSeeder.LoadAsync(_host, owner.Id)).Owner.PasswordHash.ShouldStartWith("$argon2id$v=19$");
        await LogInAsync(email, OwnerSeeder.Password, 401);
        await LogInAsync(email, NewPassword, 201);
    }

    private Task<IScenarioResult> LogInAsync(string email, string password, int status) =>
        _host.Scenario(s =>
        {
            s.Post.Json(new { email, password }).ToUrl("/v1/identity/sessions");
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.StatusCodeShouldBe(status);
        });

    private static IPAddress NewIp() => new(Guid.NewGuid().ToByteArray());
}
