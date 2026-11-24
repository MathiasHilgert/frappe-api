using System.Net;
using System.Text.Json;
using Frappe.Access.Contracts;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Sessions.StepUp;
using Frappe.Modules;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The step-up route and the guard of later routes (S14) over the real session authentication: what the route hands the use case, its 201 without
/// <c>Location</c> or cache, its refusals as Problems, and the 403 a guarded route answers without an open window.
/// </summary>
public sealed class IdentityStepUpRouteTests
{
    private const string Path = "/v1/identity/sessions/current/step-ups";
    private const string Guarded = "/v1/probe/step-up";

    private static HttpRequestMessage Request(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

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
    public async Task Stepping_up_hands_the_use_case_the_caller_and_the_session_and_answers_201_with_the_window_and_no_location_or_cache()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (principal, session, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Post, Path, token, new { password = "my password" }));

        // Assert
        var command = fakes.Commands.Single();
        var body = await ReadAsync(response);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
            await Assert.That(response.Headers.Location).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(body.GetProperty("validUntil").GetDateTimeOffset()).IsEqualTo(Fakes.ValidUntil.ToDateTimeOffset());
            await Assert.That(fakes.Callers.Single()).IsEqualTo((principal.Id, session.Id));
            await Assert.That(command.Password).IsEqualTo("my password");
            await Assert.That(command.ToString()).DoesNotContain("my password");
        }
    }

    [Test]
    public async Task Stepping_up_without_a_session_is_401_and_never_reaches_the_use_case()
    {
        // Arrange
        var fakes = new Fakes();
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Post, Path, null, new { password = "a" }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(fakes.Commands).IsEmpty();
        }
    }

    [Test]
    public async Task A_wrong_password_is_the_422_with_its_code_and_text_and_a_throttle_is_the_429_with_retry_after()
    {
        // Arrange
        var fakes = new Fakes { Answer = Error.RuleViolation("identity.password.incorrect") };
        await using var host = await IdentitySessionHost.StartAsync(extra: new FakesModule(fakes));
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var wrong = await client.SendAsync(Request(HttpMethod.Post, Path, token, new { password = "a" }));
        var error = (await ReadAsync(wrong)).GetProperty("errors")[0];
        fakes.Answer = new RateLimitedError(PasswordRecheck.ThrottledCode, TimeSpan.FromMinutes(5));
        using var throttled = await client.SendAsync(Request(HttpMethod.Post, Path, token, new { password = "a" }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(wrong.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("identity.password.incorrect");
            await Assert.That(throttled.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(throttled.Headers.RetryAfter?.Delta).IsEqualTo(TimeSpan.FromMinutes(5));
        }
    }

    [Test]
    public async Task A_guarded_route_answers_403_with_its_code_and_text_while_the_session_has_no_window_and_never_runs()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, _, token) = host.Seed();
        using var client = host.App.GetTestClient();

        // Act
        using var response = await client.SendAsync(Request(HttpMethod.Get, Guarded, token));
        using var anonymous = await client.SendAsync(Request(HttpMethod.Get, Guarded, null));

        // Assert
        var error = (await ReadAsync(response)).GetProperty("errors")[0];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo("access.step_up.required");
            await Assert.That(error.GetProperty("message").GetString()).IsNotEmpty();
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
    }

    [Test]
    public async Task A_guarded_route_runs_inside_the_window_and_is_403_again_once_the_window_ends_however_much_the_session_was_used()
    {
        // Arrange
        await using var host = await IdentitySessionHost.StartAsync();
        var (_, session, token) = host.Seed();
        session.StepUp(IdentitySessionHost.Start);
        using var client = host.App.GetTestClient();

        // Act
        using var inside = await client.SendAsync(Request(HttpMethod.Get, Guarded, token));
        host.Time.Advance(TimeSpan.FromMinutes(4));
        using var used = await client.SendAsync(Request(HttpMethod.Get, Guarded, token));
        host.Time.Advance(TimeSpan.FromMinutes(1));
        using var ended = await client.SendAsync(Request(HttpMethod.Get, Guarded, token));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(inside.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(used.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(ended.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>What the faked use case saw and the answer it gives.</summary>
    private sealed class Fakes
    {
        /// <summary>The principal and session the use cases saw through the caller.</summary>
        public List<(Guid Principal, Guid Session)> Callers { get; } = [];

        public void Seen(ICaller caller) => Callers.Add((caller.RequiredPrincipalId, caller.RequiredSessionId));

        public static readonly Instant ValidUntil = Instant.FromUtc(2026, 11, 24, 12, 5);

        public List<StepUp> Commands { get; } = [];

        public Error? Answer { get; set; }
    }

    /// <summary>Registers the faked use case.</summary>
    private sealed class FakesModule(Fakes fakes) : IModule
    {
        public void AddServices(IServiceCollection services) =>
            services.AddFakeCommand<StepUp, SteppedUp>(new Handler(fakes), fakes.Seen);
    }

    /// <summary>The step-up use case, standing in for the real one.</summary>
    private sealed class Handler(Fakes fakes) : ICommandHandler<StepUp, SteppedUp>
    {
        public Task<Result<SteppedUp>> HandleAsync(StepUp command, CancellationToken cancellationToken)
        {
            fakes.Commands.Add(command);
            return Task.FromResult<Result<SteppedUp>>(fakes.Answer is null ? new SteppedUp(Fakes.ValidUntil) : fakes.Answer);
        }
    }
}
