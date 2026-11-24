using System.Net;
using System.Text.Json;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Credentials.ChangePassword;
using Frappe.Identity.Application.Owners.RequestPasswordReset;
using Frappe.Identity.Application.Owners.ResetPassword;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The password routes (S11, S12) over the real session authentication with the use cases faked: what each route hands the use case, the status
/// each answers, no <c>Location</c> and no cache, the floor on the reset request, and the use case's refusals as Problems with their codes.
/// </summary>
public sealed class IdentityPasswordRouteTests
{
    private static readonly Guid ResetId = Guid.Parse("0198f2a0-0000-7000-8000-0000000000bb");

    private static HttpRequestMessage Post(string path, object? body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return request;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    public async Task Changing_the_password_hands_the_use_case_the_caller_and_the_session_and_answers_204_without_cache_or_location()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(
            Post("/v1/identity/me/password-changes", new { currentPassword = "old one", newPassword = "new one" }, token));

        // Assert
        var command = fakes.Changes.Single();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(command.PrincipalId).IsEqualTo(principal.Id);
            await Assert.That(command.SessionId).IsEqualTo(session.Id);
            await Assert.That(command.CurrentPassword).IsEqualTo("old one");
            await Assert.That(command.NewPassword).IsEqualTo("new one");
            await Assert.That(command.ToString()).DoesNotContain("old one").And.DoesNotContain("new one");
        }
    }

    [Test]
    public async Task Changing_the_password_without_a_session_is_401_and_never_reaches_the_use_case()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/me/password-changes", new { currentPassword = "a", newPassword = "b" }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fakes.Changes).IsEmpty();
        }
    }

    [Test]
    public async Task A_wrong_current_password_is_the_422_with_its_code_and_text_and_a_throttle_is_the_429_with_retry_after()
    {
        // Arrange
        var fakes = new Fakes { Changed = Error.RuleViolation("identity.password.incorrect") };
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var wrong = await client.SendAsync(Post("/v1/identity/me/password-changes", new { currentPassword = "a", newPassword = "b" }, token));
        var wrongError = (await ReadAsync(wrong)).GetProperty("errors")[0];
        fakes.Changed = new RateLimitedError(PasswordRecheck.ThrottledCode, TimeSpan.FromMinutes(5));
        using var throttled = await client.SendAsync(
            Post("/v1/identity/me/password-changes", new { currentPassword = "a", newPassword = "b" }, token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(wrongError.GetProperty("code").GetString()).IsEqualTo("identity.password.incorrect");
            await Assert.That(wrongError.GetProperty("message").GetString()).IsEqualTo("The current password is not right.");
            await Assert.That(throttled.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(throttled.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromMinutes(5));
        }
    }

    [Test]
    public async Task Asking_for_a_reset_answers_202_with_the_id_and_no_location_and_never_sooner_than_the_floor()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();
        host.Time.FrozenTimestamp = 0;

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/password-resets", new { email = "ana@example.com" }));

        // Assert
        var body = await ReadAsync(response);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(ResetId);
            await Assert.That(fakes.Requests.Single().Email).IsEqualTo("ana@example.com");
            await Assert.That(host.Time.Waits).Contains(PasswordResetLimits.Floor);
        }
    }

    [Test]
    public async Task A_reset_request_whose_use_case_fails_is_held_to_the_same_floor()
    {
        // Arrange
        var fakes = new Fakes { Requested = new UnavailableError("identity.attempts.unavailable") };
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();
        host.Time.FrozenTimestamp = 0;

        // Act
        using var response = await client.SendAsync(Post("/v1/identity/password-resets", new { email = "ana@example.com" }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(host.Time.Waits).Contains(PasswordResetLimits.Floor);
        }
    }

    [Test]
    public async Task Confirming_a_reset_hands_the_use_case_the_id_as_text_and_answers_204()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();

        // Act
        var body = new { code = "482913", newPassword = "new one" };
        using var response = await client.SendAsync(Post($"/v1/identity/password-resets/{ResetId}/confirmation", body));
        using var malformed = await client.SendAsync(Post("/v1/identity/password-resets/junk/confirmation", body));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(fakes.Resets.Select(c => c.ResetId!)).IsEquivalentTo([ResetId.ToString(), "junk"]);
            await Assert.That(fakes.Resets[0].Code).IsEqualTo("482913");
            await Assert.That(fakes.Resets[0].NewPassword).IsEqualTo("new one");
            await Assert.That(fakes.Resets[0].ToString()).DoesNotContain("482913").And.DoesNotContain("new one");
        }
    }

    [Test]
    public async Task A_refused_confirmation_is_the_422_with_the_code_text_and_a_down_limiter_is_the_503()
    {
        // Arrange
        var fakes = new Fakes { Confirmed = Error.RuleViolation("identity.code.invalid") };
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();
        var request = new { code = "000000", newPassword = "new one" };

        // Act
        using var invalid = await client.SendAsync(Post($"/v1/identity/password-resets/{ResetId}/confirmation", request));
        var error = (await ReadAsync(invalid)).GetProperty("errors")[0];
        fakes.Confirmed = new UnavailableError("identity.attempts.unavailable");
        using var unavailable = await client.SendAsync(Post($"/v1/identity/password-resets/{ResetId}/confirmation", request));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("identity.code.invalid");
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
    }

    /// <summary>What the faked use cases saw and the answers they give.</summary>
    private sealed class Fakes
    {
        public List<ChangePassword> Changes { get; } = [];

        public List<RequestPasswordReset> Requests { get; } = [];

        public List<ResetPassword> Resets { get; } = [];

        public Error? Changed { get; set; }

        public Error? Requested { get; init; }

        public Error? Confirmed { get; set; }
    }

    /// <summary>Registers the faked use cases of the password routes.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddFakeCommand<ChangePassword, Unit>(new Change(fakes));
            services.AddFakeCommand<RequestPasswordReset, PasswordResetRequested>(new Request(fakes));
            services.AddFakeCommand<ResetPassword, Unit>(new Confirm(fakes));
        }
    }

    /// <summary>The change use case, standing in for the real one.</summary>
    private sealed class Change(Fakes fakes) : ICommandHandler<ChangePassword, Unit>
    {
        public Task<Result<Unit>> HandleAsync(ChangePassword command, CancellationToken cancellationToken)
        {
            fakes.Changes.Add(command);
            return Task.FromResult<Result<Unit>>(fakes.Changed is null ? Unit.Value : fakes.Changed);
        }
    }

    /// <summary>The reset request use case, standing in for the real one.</summary>
    private sealed class Request(Fakes fakes) : ICommandHandler<RequestPasswordReset, PasswordResetRequested>
    {
        public Task<Result<PasswordResetRequested>> HandleAsync(RequestPasswordReset command, CancellationToken cancellationToken)
        {
            fakes.Requests.Add(command);
            return Task.FromResult<Result<PasswordResetRequested>>(fakes.Requested is null ? new PasswordResetRequested(ResetId) : fakes.Requested);
        }
    }

    /// <summary>The reset confirmation use case, standing in for the real one.</summary>
    private sealed class Confirm(Fakes fakes) : ICommandHandler<ResetPassword, Unit>
    {
        public Task<Result<Unit>> HandleAsync(ResetPassword command, CancellationToken cancellationToken)
        {
            fakes.Resets.Add(command);
            return Task.FromResult<Result<Unit>>(fakes.Confirmed is null ? Unit.Value : fakes.Confirmed);
        }
    }
}
